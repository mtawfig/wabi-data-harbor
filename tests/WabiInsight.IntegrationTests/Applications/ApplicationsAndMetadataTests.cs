using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Applications;

/// <summary>Uses the test database itself as the "business" source: a real SQL Server with real catalog views.</summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class ApplicationsAndMetadataTests : IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly ApiFactory _factory;
    private readonly string _keyRing = Path.Combine(Path.GetTempPath(), "wi-keys-tests", Guid.NewGuid().ToString("N"));

    public ApplicationsAndMetadataTests(SqlServerTestDatabase db)
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

    private string Source(string tag) => new SqlConnectionStringBuilder(_db.ConnectionString) { WorkstationID = tag }.ConnectionString;

    [Fact]
    public async Task Admin_CreatesApplication_TestsConnection_RefreshesMetadata_ApprovesObjects_WithoutExposingTheConnectionString()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);

        // Test an unsaved connection string first.
        var probe = await alice.PostJsonAsync("/api/applications/test-connection", new { databaseType = "SqlServer", connectionString = Source("erp") });
        Assert.Equal(HttpStatusCode.OK, probe.StatusCode);
        var probeResult = await probe.ReadJsonAsync();
        Assert.True(probeResult.GetProperty("succeeded").GetBoolean());
        Assert.False(probeResult.GetProperty("readOnlyVerified").GetBoolean()); // sa is not read-only
        Assert.Contains("account_not_read_only", probeResult.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()));

        var create = await alice.PostJsonAsync("/api/applications", new { applicationCode = "erp", applicationName = "ERP", applicationNameAr = "نظام الموارد", description = "Finance ERP", databaseType = "SqlServer", connectionString = Source("erp"), connectionHint = "erp-db / Finance" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.ReadJsonAsync();
        var applicationId = created.GetProperty("applicationId").GetGuid();
        Assert.Equal("ERP", created.GetProperty("applicationCode").GetString());
        Assert.True(created.GetProperty("connectionConfigured").GetBoolean());
        Assert.Equal("erp-db / Finance", created.GetProperty("connectionHint").GetString());
        var body = await create.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connectionString", body, StringComparison.Ordinal);

        // Stored at rest: encrypted payload and a fingerprint, never the plain text.
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            var row = await context.Applications.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.ApplicationId == applicationId);
            Assert.NotNull(row.EncryptedConnectionString);
            Assert.DoesNotContain("WabiInsight_Dev", row.EncryptedConnectionString, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Password", row.EncryptedConnectionString, StringComparison.OrdinalIgnoreCase);
            Assert.Matches("^[0-9a-f]{64}$", row.ConnectionFingerprint!);
        }

        // Stored connection test records the outcome.
        var stored = await (await alice.PostJsonAsync($"/api/applications/{applicationId}/test-connection", new { })).ReadJsonAsync();
        Assert.True(stored.GetProperty("succeeded").GetBoolean());
        var afterTest = await (await alice.GetAsync($"/api/applications/{applicationId}")).ReadJsonAsync();
        Assert.True(afterTest.GetProperty("lastConnectionTestSucceeded").GetBoolean());

        // Approval requires a connection and is explicit.
        Assert.False(afterTest.GetProperty("isApproved").GetBoolean());
        var approved = await (await alice.PostJsonAsync($"/api/applications/{applicationId}/approval", new { isApproved = true })).ReadJsonAsync();
        Assert.True(approved.GetProperty("isApproved").GetBoolean());

        // Metadata discovery over the real catalog.
        var refresh = await alice.PostJsonAsync($"/api/metadata/applications/{applicationId}/refresh", new { });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var diff = await refresh.ReadJsonAsync();
        Assert.True(diff.GetProperty("objectsAdded").GetInt32() >= 10);
        Assert.True(diff.GetProperty("columnsAdded").GetInt32() > 50);
        Assert.True(diff.GetProperty("relationshipsAdded").GetInt32() >= 1);

        var objects = await (await alice.GetAsync($"/api/metadata/applications/{applicationId}/objects?search=SupportTickets&pageSize=10")).ReadJsonAsync();
        var tickets = objects.GetProperty("items").EnumerateArray().Single(o => o.GetProperty("objectName").GetString() == "SupportTickets");
        Assert.False(tickets.GetProperty("isApproved").GetBoolean()); // new objects stay unapproved
        Assert.Equal("Table", tickets.GetProperty("objectType").GetString());
        var objectId = tickets.GetProperty("objectId").GetGuid();

        var detail = await (await alice.GetAsync($"/api/metadata/objects/{objectId}")).ReadJsonAsync();
        var columns = detail.GetProperty("columns").EnumerateArray().ToList();
        var ticketId = columns.Single(c => c.GetProperty("columnName").GetString() == "TicketId");
        Assert.True(ticketId.GetProperty("isPrimaryKey").GetBoolean());
        Assert.Equal("uniqueidentifier", ticketId.GetProperty("dataType").GetString());
        Assert.Contains(detail.GetProperty("relationships").EnumerateArray(), r => r.GetProperty("toObject").GetString() == "dbo.SupportTickets" || r.GetProperty("fromObject").GetString() == "dbo.SupportTickets");

        // Names, descriptions, classification and approval.
        var update = await alice.PutAsJsonAsync(new Uri($"/api/metadata/objects/{objectId}", UriKind.Relative), new { displayNameEn = "Support tickets", displayNameAr = "تذاكر الدعم", businessDescription = "One row per ticket.", isApproved = true });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("تذاكر الدعم", (await update.ReadJsonAsync()).GetProperty("summary").GetProperty("displayNameAr").GetString());
        var subject = columns.Single(c => c.GetProperty("columnName").GetString() == "Subject").GetProperty("columnId").GetGuid();
        var column = await (await alice.PutAsJsonAsync(new Uri($"/api/metadata/columns/{subject}", UriKind.Relative), new { displayNameEn = "Subject", displayNameAr = "الموضوع", classification = "Confidential", isApproved = true })).ReadJsonAsync();
        Assert.Equal("Confidential", column.GetProperty("classification").GetString());
        var all = await (await alice.PostJsonAsync($"/api/metadata/objects/{objectId}/approve", new { approve = true })).ReadJsonAsync();
        Assert.All(all.GetProperty("columns").EnumerateArray(), c => Assert.True(c.GetProperty("isApproved").GetBoolean()));

        var application = await (await alice.GetAsync($"/api/applications/{applicationId}")).ReadJsonAsync();
        Assert.True(application.GetProperty("objectCount").GetInt32() >= 10);
        Assert.Equal(1, application.GetProperty("approvedObjectCount").GetInt32());
        Assert.NotNull(application.GetProperty("lastMetadataRefreshAt").GetString());
    }

    [Fact]
    public async Task Refresh_DiffsNewAndRemovedObjects_KeepingApprovalsAndFlaggingRemovals()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        var created = await (await alice.PostJsonAsync("/api/applications", new { applicationCode = "diff", applicationName = "Diff", applicationNameAr = "فرق", databaseType = "SqlServer", connectionString = Source("diff") })).ReadJsonAsync();
        var applicationId = created.GetProperty("applicationId").GetGuid();
        await alice.PostJsonAsync($"/api/metadata/applications/{applicationId}/refresh", new { });

        var table = $"Temp_{Guid.NewGuid():N}";
        await using (var connection = new SqlConnection(_db.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE TABLE dbo.[{table}] (Id int NOT NULL PRIMARY KEY, Name nvarchar(50) NULL)";
            await command.ExecuteNonQueryAsync();
        }

        var second = await (await alice.PostJsonAsync($"/api/metadata/applications/{applicationId}/refresh", new { })).ReadJsonAsync();
        Assert.Equal(1, second.GetProperty("objectsAdded").GetInt32());
        Assert.Equal(2, second.GetProperty("columnsAdded").GetInt32());
        var added = (await (await alice.GetAsync($"/api/metadata/applications/{applicationId}/objects?search={table}")).ReadJsonAsync()).GetProperty("items").EnumerateArray().Single();
        var objectId = added.GetProperty("objectId").GetGuid();
        await alice.PostJsonAsync($"/api/metadata/objects/{objectId}/approve", new { approve = true });

        await using (var connection = new SqlConnection(_db.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP TABLE dbo.[{table}]";
            await command.ExecuteNonQueryAsync();
        }

        var third = await (await alice.PostJsonAsync($"/api/metadata/applications/{applicationId}/refresh", new { })).ReadJsonAsync();
        Assert.Equal(1, third.GetProperty("objectsRemoved").GetInt32());
        var visible = await (await alice.GetAsync($"/api/metadata/applications/{applicationId}/objects?search={table}")).ReadJsonAsync();
        Assert.Equal(0, visible.GetProperty("totalCount").GetInt32());
        var removed = (await (await alice.GetAsync($"/api/metadata/applications/{applicationId}/objects?search={table}&includeRemoved=true")).ReadJsonAsync()).GetProperty("items").EnumerateArray().Single();
        Assert.False(removed.GetProperty("isPresent").GetBoolean());
        Assert.True(removed.GetProperty("isApproved").GetBoolean()); // kept, only flagged
        var reapprove = await alice.PutAsJsonAsync(new Uri($"/api/metadata/objects/{objectId}", UriKind.Relative), new { isApproved = true });
        Assert.Equal(HttpStatusCode.BadRequest, reapprove.StatusCode);
    }

    [Fact]
    public async Task TenantBoundaries_DuplicateSources_AndPlatformAdministratorsCannotBrowse()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        var source = Source("shared");
        var created = await (await alice.PostJsonAsync("/api/applications", new { applicationCode = "crm", applicationName = "CRM", applicationNameAr = "العملاء", databaseType = "SqlServer", connectionString = source })).ReadJsonAsync();
        var applicationId = created.GetProperty("applicationId").GetGuid();

        // Organization B (Bob is not an administrator; make one) cannot see A's application nor register the same source.
        var bobAdmin = await MakeAdminOfBAsync();
        var list = await (await bobAdmin.GetAsync("/api/applications")).ReadJsonAsync();
        Assert.DoesNotContain(list.EnumerateArray(), a => a.GetProperty("applicationId").GetGuid() == applicationId);
        Assert.Equal(HttpStatusCode.NotFound, (await bobAdmin.GetAsync($"/api/applications/{applicationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bobAdmin.PostJsonAsync($"/api/metadata/applications/{applicationId}/refresh", new { })).StatusCode);
        // The same source written differently (key order, casing, a different application name) must still collide.
        var parts = new SqlConnectionStringBuilder(source);
        var rewritten = $"database={parts.InitialCatalog};server={parts.DataSource};user id={parts.UserID};password={parts.Password};TrustServerCertificate={parts.TrustServerCertificate};Encrypt={parts.Encrypt};Workstation ID={parts.WorkstationID};Application Name=whatever";
        var duplicate = await bobAdmin.PostJsonAsync("/api/applications", new { applicationCode = "crm2", applicationName = "CRM copy", applicationNameAr = "نسخة", databaseType = "SqlServer", connectionString = rewritten });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("connection_already_registered", await duplicate.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Non-administrators and platform administrators have no application endpoints.
        using var platform = await LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync("/api/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync($"/api/metadata/applications/{applicationId}/objects")).StatusCode);
        using var bob = await LoginAsync(TestData.BobEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/applications")).StatusCode);

        // The built-in application is protected and the reserved type cannot be created.
        var all = await (await alice.GetAsync("/api/applications")).ReadJsonAsync();
        var builtIn = all.EnumerateArray().FirstOrDefault(a => a.ValueKind != JsonValueKind.Undefined && a.GetProperty("isBuiltIn").GetBoolean());
        if (builtIn.ValueKind == JsonValueKind.Object)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostJsonAsync($"/api/applications/{builtIn.GetProperty("applicationId").GetGuid()}/approval", new { isApproved = false })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostJsonAsync("/api/applications", new { applicationCode = "pi", applicationName = "x", applicationNameAr = "x", databaseType = "PlatformInsights" })).StatusCode);
        var badString = await alice.PostJsonAsync("/api/applications", new { applicationCode = "bad", applicationName = "x", applicationNameAr = "x", databaseType = "SqlServer", connectionString = "Server=only" });
        Assert.Equal(HttpStatusCode.BadRequest, badString.StatusCode);
        Assert.Contains("connection_string_database_required", await badString.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SupportGrant_CanRefreshMetadata_AndDiagnoseConnection_OnlyForTheListedApplication()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        var created = await (await alice.PostJsonAsync("/api/applications", new { applicationCode = "wh", applicationName = "Warehouse", applicationNameAr = "المستودع", databaseType = "SqlServer", connectionString = Source("wh") })).ReadJsonAsync();
        var applicationId = created.GetProperty("applicationId").GetGuid();
        var ticket = await (await alice.PostJsonAsync("/api/support/tickets", new { ticketType = "Request", impact = "Medium", subject = "Please refresh our warehouse metadata", description = "x", requiresSupportAccess = true })).ReadJsonAsync();
        var ticketId = ticket.GetProperty("summary").GetProperty("ticketId").GetGuid();

        using var platform = await LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        var grant = await (await platform.PostJsonAsync($"/api/platform/support/tickets/{ticketId}/access-grants", new { operations = new object[] { new { operation = "RefreshMetadata", targetReferences = new[] { applicationId.ToString() } }, new { operation = "DiagnoseApplicationConnection", targetReferences = new[] { applicationId.ToString() } } }, reason = "Requested." })).ReadJsonAsync();
        var grantId = grant.GetProperty("grantId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/support/access-grants/{grantId}/approve", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await platform.PostJsonAsync($"/api/platform/support/access-grants/{grantId}/enter", new { })).StatusCode);

        var refresh = await platform.PostJsonAsync("/api/platform/support/session/execute", new { operation = "RefreshMetadata", targetReference = applicationId.ToString() });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.True((await refresh.ReadJsonAsync()).GetProperty("result").GetProperty("objectsAdded").GetInt32() >= 10);
        var diagnose = await platform.PostJsonAsync("/api/platform/support/session/execute", new { operation = "DiagnoseApplicationConnection", targetReference = applicationId.ToString() });
        Assert.Equal(HttpStatusCode.OK, diagnose.StatusCode);
        var diagnosis = await diagnose.ReadJsonAsync();
        Assert.True(diagnosis.GetProperty("result").GetProperty("succeeded").GetBoolean());
        Assert.DoesNotContain("Password", await diagnose.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var other = await platform.PostJsonAsync("/api/platform/support/session/execute", new { operation = "RefreshMetadata", targetReference = Guid.NewGuid().ToString() });
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        var objects = await (await alice.GetAsync($"/api/metadata/applications/{applicationId}/objects?pageSize=5")).ReadJsonAsync();
        Assert.True(objects.GetProperty("totalCount").GetInt32() >= 10);
        Assert.Equal(HttpStatusCode.OK, (await platform.PostJsonAsync($"/api/platform/support/access-grants/{grantId}/complete", new { note = "done" })).StatusCode);
    }

    private async Task<HttpClient> MakeAdminOfBAsync()
    {
        // Organization B has no administrator in the seed; add a dedicated one without touching Bob (other tests rely on him being a plain user).
        var email = $"admin.{Guid.NewGuid():N}@org-b.test";
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            var userId = Core.Common.Ids.New();
            var now = DateTimeOffset.UtcNow;
            context.OrganizationUsers.Add(TestData.NewUser(userId, _db.Data.OrganizationBId, email, "Admin B"));
            context.LoginIdentities.Add(TestData.Identity(email, Core.Tenancy.PrincipalType.OrganizationUser, userId, _db.Data.OrganizationBId, now));
            context.UserRoles.Add(new Core.Entities.Organizations.UserRole { OrganizationId = _db.Data.OrganizationBId, UserId = userId, RoleId = _db.Data.RoleAdminBId, AssignedAt = now, AssignedByAdministratorId = _db.Data.AdministratorId });
            await context.SaveChangesAsync();
        }

        return await LoginAsync(email);
    }
}
