using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Permissions;

/// <summary>Permissions model (specification §10): role grants, row-scope mappings, effective permissions, support operation.</summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class PermissionsTests : IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly ApiFactory _factory;
    private readonly string _keyRing = Path.Combine(Path.GetTempPath(), "wi-keys-tests", Guid.NewGuid().ToString("N"));

    public PermissionsTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new ApiFactory(db.ConnectionString, new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = _keyRing });
    }

    public void Dispose()
    {
        _factory.Dispose();
        if (Directory.Exists(_keyRing))
        {
            Directory.Delete(_keyRing, recursive: true);
        }
    }

    private async Task<HttpClient> LoginAsync(string email, string password = TestData.UserPassword)
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, password)).StatusCode);
        return client;
    }

    private static Task<HttpResponseMessage> PutJsonAsync<T>(HttpClient client, string path, T body) => client.PutAsJsonAsync(new Uri(path, UriKind.Relative), body);

    /// <summary>Registers the test database as an approved application with the SupportTickets object (and all its columns) approved.</summary>
    private async Task<(Guid ApplicationId, Guid ObjectId, List<JsonElement> Columns)> PrepareApplicationAsync(HttpClient alice, string tag)
    {
        var source = new SqlConnectionStringBuilder(_db.ConnectionString) { WorkstationID = tag }.ConnectionString;
        var created = await (await alice.PostJsonAsync("/api/applications", new { applicationCode = tag, applicationName = tag, applicationNameAr = tag, databaseType = "SqlServer", connectionString = source })).ReadJsonAsync();
        var applicationId = created.GetProperty("applicationId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/applications/{applicationId}/test-connection", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/applications/{applicationId}/approval", new { isApproved = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/metadata/applications/{applicationId}/refresh", new { })).StatusCode);
        var objects = await (await alice.GetAsync($"/api/metadata/applications/{applicationId}/objects?search=SupportTickets&pageSize=10")).ReadJsonAsync();
        var objectId = objects.GetProperty("items").EnumerateArray().Single(o => o.GetProperty("schemaName").GetString() == "dbo" && o.GetProperty("objectName").GetString() == "SupportTickets").GetProperty("objectId").GetGuid();
        var approved = await (await alice.PostJsonAsync($"/api/metadata/objects/{objectId}/approve", new { approve = true })).ReadJsonAsync();
        return (applicationId, objectId, approved.GetProperty("columns").EnumerateArray().ToList());
    }

    private async Task<(Guid UserId, string Email)> InsertUserAsync(Guid organizationId, string fullName)
    {
        var email = $"{Guid.NewGuid():N}@org-perm.test";
        var userId = Core.Common.Ids.New();
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        context.OrganizationUsers.Add(TestData.NewUser(userId, organizationId, email, fullName));
        context.LoginIdentities.Add(TestData.Identity(email, Core.Tenancy.PrincipalType.OrganizationUser, userId, organizationId, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
        return (userId, email);
    }

    [Fact]
    public async Task Grants_Mappings_AndEffectivePermissions_FollowTheSpecification()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        var (applicationId, objectId, columns) = await PrepareApplicationAsync(alice, "perm");
        var ticketId = columns.Single(c => c.GetProperty("columnName").GetString() == "TicketId").GetProperty("columnId").GetGuid();
        var subject = columns.Single(c => c.GetProperty("columnName").GetString() == "Subject").GetProperty("columnId").GetGuid();
        var orgColumn = columns.Single(c => c.GetProperty("columnName").GetString() == "OrganizationId").GetProperty("columnId").GetGuid();

        // A custom role, a department and a user in it.
        var role = await (await alice.PostJsonAsync("/api/roles", new { roleCode = $"Fin{Guid.NewGuid():N}"[..12], nameEn = "Finance", nameAr = "المالية", description = "Finance analysts" })).ReadJsonAsync();
        var roleId = role.GetProperty("roleId").GetGuid();
        var roleCode = role.GetProperty("roleCode").GetString()!;
        var department = await (await alice.PostJsonAsync("/api/departments", new { departmentCode = $"D{Guid.NewGuid():N}"[..8], departmentNameEn = "Finance", departmentNameAr = "المالية" })).ReadJsonAsync();
        var departmentId = department.GetProperty("departmentId").GetGuid();
        var (userId, email) = await InsertUserAsync(_db.Data.OrganizationAId, "Fin User");
        var update = await PutJsonAsync(alice, $"/api/users/{userId}", new { fullName = "Fin User", employeeNumber = "E-100", departmentId, preferredLanguage = "en", roleCodes = new[] { roleCode } });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var fin = await LoginAsync(email);

        // Nothing granted yet: the matrix lists approved objects, the user sees no objects.
        var matrix = await (await alice.GetAsync($"/api/permissions/roles/{roleId}/applications/{applicationId}")).ReadJsonAsync();
        var row = matrix.GetProperty("objects").EnumerateArray().Single(o => o.GetProperty("objectId").GetGuid() == objectId);
        Assert.False(row.GetProperty("granted").GetBoolean());
        Assert.True(row.GetProperty("isApproved").GetBoolean());
        Assert.Empty((await (await fin.GetAsync("/api/permissions/me")).ReadJsonAsync()).GetProperty("objects").EnumerateArray());

        // Validation: a grant needs a row scope and approved columns; unapproved objects cannot be granted.
        Assert.Equal(HttpStatusCode.BadRequest, (await PutJsonAsync(alice, $"/api/permissions/roles/{roleId}/objects/{objectId}", new { granted = true, allColumns = true })).StatusCode);
        var badColumn = await PutJsonAsync(alice, $"/api/permissions/roles/{roleId}/objects/{objectId}", new { granted = true, allColumns = false, columnIds = new[] { Guid.NewGuid() }, rowScope = "All" });
        Assert.Equal(HttpStatusCode.BadRequest, badColumn.StatusCode);
        Assert.Contains("column_not_approved", await badColumn.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var unapproved = (await (await alice.GetAsync($"/api/metadata/applications/{applicationId}/objects?pageSize=50")).ReadJsonAsync()).GetProperty("items").EnumerateArray().First(o => !o.GetProperty("isApproved").GetBoolean()).GetProperty("objectId").GetGuid();
        var notApproved = await PutJsonAsync(alice, $"/api/permissions/roles/{roleId}/objects/{unapproved}", new { granted = true, allColumns = true, rowScope = "All" });
        Assert.Equal(HttpStatusCode.BadRequest, notApproved.StatusCode);
        Assert.Contains("object_not_approved", await notApproved.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Grant two columns with the Department scope: columns are visible but, with no mapping, the scope yields no rows.
        var grant = await PutJsonAsync(alice, $"/api/permissions/roles/{roleId}/objects/{objectId}", new { granted = true, allColumns = false, columnIds = new[] { ticketId, subject }, rowScope = "Department" });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        var granted = await grant.ReadJsonAsync();
        Assert.True(granted.GetProperty("granted").GetBoolean());
        Assert.Equal("Department", granted.GetProperty("rowScope").GetString());
        Assert.Equal(2, granted.GetProperty("columns").EnumerateArray().Count(c => c.GetProperty("granted").GetBoolean()));
        Assert.False(granted.GetProperty("mapping").GetProperty("department").GetBoolean());

        var mine = await (await fin.GetAsync("/api/permissions/me")).ReadJsonAsync();
        var access = mine.GetProperty("objects").EnumerateArray().Single();
        Assert.Equal(objectId, access.GetProperty("objectId").GetGuid());
        Assert.Equal(2, access.GetProperty("columns").GetArrayLength());
        Assert.False(access.GetProperty("yieldsRows").GetBoolean());
        Assert.Equal("Department", access.GetProperty("unmappedScopes").EnumerateArray().Single().GetString());
        Assert.Equal(department.GetProperty("departmentCode").GetString(), mine.GetProperty("context").GetProperty("departmentCode").GetString());
        Assert.Equal("E-100", mine.GetProperty("context").GetProperty("employeeNumber").GetString());

        // Mapping the department column makes the scope effective; invalid columns are rejected.
        Assert.Equal(HttpStatusCode.BadRequest, (await PutJsonAsync(alice, $"/api/permissions/objects/{objectId}/row-mapping", new { departmentColumnId = Guid.NewGuid() })).StatusCode);
        var mapping = await PutJsonAsync(alice, $"/api/permissions/objects/{objectId}/row-mapping", new { departmentColumnId = orgColumn });
        Assert.Equal(HttpStatusCode.OK, mapping.StatusCode);
        Assert.Equal(orgColumn, (await mapping.ReadJsonAsync()).GetProperty("departmentColumnId").GetGuid());
        access = (await (await fin.GetAsync("/api/permissions/me")).ReadJsonAsync()).GetProperty("objects").EnumerateArray().Single();
        Assert.True(access.GetProperty("yieldsRows").GetBoolean());
        Assert.Equal("Department", access.GetProperty("rowScopes").EnumerateArray().Single().GetString());
        Assert.Contains(roleCode, access.GetProperty("viaRoles").EnumerateArray().Select(r => r.GetString()));

        // The administrator previews the same result; a plain user cannot preview others.
        var preview = await (await alice.GetAsync($"/api/permissions/users/{userId}/effective")).ReadJsonAsync();
        Assert.Equal(userId, preview.GetProperty("userId").GetGuid());
        Assert.Single(preview.GetProperty("objects").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await fin.GetAsync($"/api/permissions/users/{_db.Data.UserA1Id}/effective")).StatusCode);

        // Management is automatic: every approved object, every approved column, all rows; it cannot be edited.
        var management = await (await alice.GetAsync("/api/roles")).ReadJsonAsync();
        var managementId = management.EnumerateArray().Single(r => r.GetProperty("roleCode").GetString() == "Management").GetProperty("roleId").GetGuid();
        var managementMatrix = await (await alice.GetAsync($"/api/permissions/roles/{managementId}/applications/{applicationId}")).ReadJsonAsync();
        Assert.True(managementMatrix.GetProperty("isSystemManagement").GetBoolean());
        Assert.All(managementMatrix.GetProperty("objects").EnumerateArray().Where(o => o.GetProperty("isApproved").GetBoolean()), o => Assert.True(o.GetProperty("granted").GetBoolean()));
        var automatic = await PutJsonAsync(alice, $"/api/permissions/roles/{managementId}/objects/{objectId}", new { granted = true, allColumns = true, rowScope = "Self" });
        Assert.Equal(HttpStatusCode.BadRequest, automatic.StatusCode);
        Assert.Contains("management_role_automatic", await automatic.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await PutJsonAsync(alice, $"/api/users/{userId}", new { fullName = "Fin User", employeeNumber = "E-100", departmentId, preferredLanguage = "en", roleCodes = new[] { roleCode, "Management" } })).StatusCode);
        using var finAgain = await LoginAsync(email); // role changes rotate the security stamp
        mine = await (await finAgain.GetAsync("/api/permissions/me")).ReadJsonAsync();
        access = mine.GetProperty("objects").EnumerateArray().Single(o => o.GetProperty("objectId").GetGuid() == objectId);
        Assert.Equal(columns.Count, access.GetProperty("columns").GetArrayLength());
        Assert.Equal("All", access.GetProperty("rowScopes").EnumerateArray().Single().GetString());
        Assert.Contains("Management", access.GetProperty("viaRoles").EnumerateArray().Select(r => r.GetString()));
        Assert.Contains("Management", mine.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));

        // Revoking the grant removes the role's rows; Management still applies.
        Assert.Equal(HttpStatusCode.OK, (await PutJsonAsync(alice, $"/api/permissions/roles/{roleId}/objects/{objectId}", new { granted = false })).StatusCode);
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            Assert.False(await context.RoleTablePermissions.IgnoreQueryFilters().AnyAsync(p => p.RoleId == roleId));
            Assert.False(await context.RoleColumnPermissions.IgnoreQueryFilters().AnyAsync(p => p.RoleId == roleId));
            Assert.False(await context.RoleRowPermissions.IgnoreQueryFilters().AnyAsync(p => p.RoleId == roleId));
            Assert.True(await context.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.EventType == "Permission.Changed" && a.EntityId == roleId.ToString()));
            Assert.True(await context.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.EventType == "Permission.RowMappingChanged" && a.EntityId == objectId.ToString()));
        }
    }

    [Fact]
    public async Task TenantBoundaries_AndRoleRestrictions()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        var (applicationId, objectId, _) = await PrepareApplicationAsync(alice, "permb");
        var analystId = (await (await alice.GetAsync("/api/roles")).ReadJsonAsync()).EnumerateArray().Single(r => r.GetProperty("roleCode").GetString() == "Analyst").GetProperty("roleId").GetGuid();

        // Organization B's administrator cannot read or write A's roles, objects or users.
        var (adminBId, adminBEmail) = await InsertUserAsync(_db.Data.OrganizationBId, "Admin B");
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            context.UserRoles.Add(new UserRole { OrganizationId = _db.Data.OrganizationBId, UserId = adminBId, RoleId = _db.Data.RoleAdminBId, AssignedAt = DateTimeOffset.UtcNow, AssignedByAdministratorId = _db.Data.AdministratorId });
            await context.SaveChangesAsync();
        }

        using var adminB = await LoginAsync(adminBEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.GetAsync($"/api/permissions/roles/{analystId}/applications/{applicationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutJsonAsync(adminB, $"/api/permissions/roles/{analystId}/objects/{objectId}", new { granted = true, allColumns = true, rowScope = "All" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.GetAsync($"/api/permissions/objects/{objectId}/row-mapping")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.GetAsync($"/api/permissions/users/{_db.Data.UserA1Id}/effective")).StatusCode);
        var roleBId = (await (await adminB.GetAsync("/api/roles")).ReadJsonAsync()).EnumerateArray().Single(r => r.GetProperty("roleCode").GetString() == "Analyst").GetProperty("roleId").GetGuid();
        var crossObject = await PutJsonAsync(adminB, $"/api/permissions/roles/{roleBId}/objects/{objectId}", new { granted = true, allColumns = true, rowScope = "All" });
        Assert.Equal(HttpStatusCode.NotFound, crossObject.StatusCode);

        // Plain users and platform administrators have no permission management; everyone in an organization can read their own.
        using var bob = await LoginAsync(TestData.BobEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync($"/api/permissions/roles/{roleBId}/applications/{applicationId}")).StatusCode);
        var bobMine = await (await bob.GetAsync("/api/permissions/me")).ReadJsonAsync();
        Assert.Equal(_db.Data.OrganizationBId, bobMine.GetProperty("organizationId").GetGuid());
        Assert.Empty(bobMine.GetProperty("objects").EnumerateArray());
        using var platform = await LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync("/api/permissions/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync($"/api/permissions/roles/{analystId}/applications/{applicationId}")).StatusCode);

        // The OrganizationAdmin role alone grants no business data.
        var aliceMine = await (await alice.GetAsync("/api/permissions/me")).ReadJsonAsync();
        Assert.DoesNotContain(aliceMine.GetProperty("objects").EnumerateArray(), o => o.GetProperty("objectId").GetGuid() == objectId);

        // Granted objects disappear when the application is un-approved.
        Assert.Equal(HttpStatusCode.OK, (await PutJsonAsync(alice, $"/api/permissions/roles/{analystId}/objects/{objectId}", new { granted = true, allColumns = true, rowScope = "All" })).StatusCode);
        var (analystUserId, analystEmail) = await InsertUserAsync(_db.Data.OrganizationAId, "Analyst A");
        Assert.Equal(HttpStatusCode.OK, (await PutJsonAsync(alice, $"/api/users/{analystUserId}", new { fullName = "Analyst A", preferredLanguage = "en", roleCodes = new[] { "Analyst" } })).StatusCode);
        using var analyst = await LoginAsync(analystEmail);
        Assert.Contains((await (await analyst.GetAsync("/api/permissions/me")).ReadJsonAsync()).GetProperty("objects").EnumerateArray(), o => o.GetProperty("objectId").GetGuid() == objectId);
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/applications/{applicationId}/approval", new { isApproved = false })).StatusCode);
        Assert.DoesNotContain((await (await analyst.GetAsync("/api/permissions/me")).ReadJsonAsync()).GetProperty("objects").EnumerateArray(), o => o.GetProperty("objectId").GetGuid() == objectId);
    }

    [Fact]
    public async Task SupportOperation_UpdateTablePermissions_RunsOnlyUnderAnApprovedGrantForTheRole()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        var (applicationId, objectId, _) = await PrepareApplicationAsync(alice, "perms");
        var role = await (await alice.PostJsonAsync("/api/roles", new { roleCode = $"Sup{Guid.NewGuid():N}"[..12], nameEn = "Support role", nameAr = "دور", description = (string?)null })).ReadJsonAsync();
        var roleId = role.GetProperty("roleId").GetGuid();
        var profile = await (await alice.GetAsync("/api/organization")).ReadJsonAsync();
        Assert.True((await PutJsonAsync(alice, "/api/organization", new { organizationName = profile.GetProperty("organizationName").GetString(), organizationNameAr = profile.GetProperty("organizationNameAr").GetString(), timeZone = profile.GetProperty("timeZone").GetString(), defaultLanguage = profile.GetProperty("defaultLanguage").GetString(), requireSecondAdminApprovalForSupportAccess = false })).IsSuccessStatusCode);
        var ticket = await alice.PostJsonAsync("/api/support/tickets", new { ticketType = "Request", impact = "Medium", subject = "Please grant the new role", description = "Configuration help.", requiresSupportAccess = true });
        var ticketId = (await ticket.ReadJsonAsync()).GetProperty("summary").GetProperty("ticketId").GetGuid();

        using var platform = await LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        await platform.PostJsonAsync("/api/platform/support/session/exit", new { });
        var payload = new { objectId, granted = true, allColumns = true, rowScope = "Self" };
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.PostJsonAsync("/api/platform/support/session/execute", new { operation = "UpdateTablePermissions", targetReference = roleId.ToString(), payload })).StatusCode);

        // Targets list the approved objects by name only (no rows, no columns).
        var targets = await (await platform.GetAsync($"/api/platform/support/tickets/{ticketId}/targets")).ReadJsonAsync();
        Assert.Contains(targets.GetProperty("objects").EnumerateArray(), o => o.GetProperty("objectId").GetGuid() == objectId && o.GetProperty("objectName").GetString() == "SupportTickets");

        var grant = await (await platform.PostJsonAsync($"/api/platform/support/tickets/{ticketId}/access-grants", new { operations = new[] { new { operation = "UpdateTablePermissions", targetReferences = new[] { roleId.ToString() } } }, reason = "Requested in the ticket.", expiryHours = 2 })).ReadJsonAsync();
        var grantId = grant.GetProperty("grantId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/support/access-grants/{grantId}/approve", new { note = "ok" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await platform.PostJsonAsync($"/api/platform/support/access-grants/{grantId}/enter", new { })).StatusCode);

        // Another role of the same organization is outside the grant.
        var analystId = (await (await alice.GetAsync("/api/roles")).ReadJsonAsync()).EnumerateArray().Single(r => r.GetProperty("roleCode").GetString() == "Analyst").GetProperty("roleId").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.PostJsonAsync("/api/platform/support/session/execute", new { operation = "UpdateTablePermissions", targetReference = analystId.ToString(), payload })).StatusCode);

        var executed = await platform.PostJsonAsync("/api/platform/support/session/execute", new { operation = "UpdateTablePermissions", targetReference = roleId.ToString(), payload });
        Assert.True(executed.StatusCode == HttpStatusCode.OK, await executed.Content.ReadAsStringAsync());
        var matrix = await (await alice.GetAsync($"/api/permissions/roles/{roleId}/applications/{applicationId}")).ReadJsonAsync();
        var row = matrix.GetProperty("objects").EnumerateArray().Single(o => o.GetProperty("objectId").GetGuid() == objectId);
        Assert.True(row.GetProperty("granted").GetBoolean());
        Assert.True(row.GetProperty("allColumns").GetBoolean());
        Assert.Equal("Self", row.GetProperty("rowScope").GetString());
        var events = await (await platform.GetAsync($"/api/platform/support/tickets/{ticketId}/events")).ReadJsonAsync();
        Assert.Contains(events.EnumerateArray(), e => e.GetProperty("eventType").GetString() == "SupportOperationExecuted" && e.GetProperty("detailsJson").GetString()!.Contains("UpdateTablePermissions", StringComparison.Ordinal));

        await platform.PostJsonAsync("/api/platform/support/session/exit", new { });
        await platform.PostJsonAsync($"/api/platform/support/access-grants/{grantId}/complete", new { });
    }
}
