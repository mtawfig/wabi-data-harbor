using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Security;

/// <summary>
/// P12.1 penetration-style sweep: every tenant-owned route is probed with organization A's identifiers by a user of
/// organization B, by an anonymous client, by a platform administrator (business data) and by an organization user
/// without the administrator role. Nothing may answer 2xx, and no response may carry organization A's data.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class CrossTenantSweepTests : IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly ApiFactory _factory;

    public CrossTenantSweepTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new ApiFactory(db.ConnectionString, new Dictionary<string, string?> { ["Storage:RootPath"] = Path.Combine(Path.GetTempPath(), "wi-sweep-storage") });
    }

    public void Dispose() => _factory.Dispose();

    private sealed record Probe(string Method, string Path, object? Body = null);

    [Fact]
    public async Task OrganizationAResources_AreInvisibleToEveryOtherPrincipal_AndSecretsNeverReachLogs()
    {
        using var alice = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await alice.LoginAsync(TestData.AliceEmail, TestData.UserPassword)).StatusCode);
        const string secret = "Sweep#Secret#Pw#2026";
        var tag = Guid.NewGuid().ToString("N")[..6];
        var source = new SqlConnectionStringBuilder(_db.ConnectionString) { WorkstationID = "sweep", Password = secret }.ConnectionString;

        // Organization A fixtures created through the API by its administrator.
        var created = await alice.PostJsonAsync("/api/applications", new { applicationCode = "sweep" + tag, applicationName = "Sweep app", applicationNameAr = "تطبيق", databaseType = "SqlServer", connectionString = source });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var applicationId = (await created.ReadJsonAsync()).GetProperty("applicationId").GetGuid();
        var departmentResponse = await alice.PostJsonAsync("/api/departments", new { departmentCode = "SW-" + tag, departmentNameEn = "Sweep Ops", departmentNameAr = "عمليات" });
        Assert.True(departmentResponse.StatusCode == HttpStatusCode.Created, await departmentResponse.Content.ReadAsStringAsync());
        var department = await departmentResponse.ReadJsonAsync();
        var departmentId = department.GetProperty("departmentId").GetGuid();
        var section = await CreatedAsync(alice, "/api/sections", new { departmentId, sectionCode = "SW-" + tag, sectionNameEn = "Sweep A", sectionNameAr = "أ" });
        var sectionId = section.GetProperty("sectionId").GetGuid();
        var ticket = await CreatedAsync(alice, "/api/support/tickets", new { ticketType = "Request", impact = "Low", subject = "Sweep ticket", description = "Created for the cross-tenant sweep.", requiresSupportAccess = false });
        var ticketId = ticket.GetProperty("summary").GetProperty("ticketId").GetGuid();
        var collection = await CreatedAsync(alice, "/api/history/collections", new { name = "Sweep collection " + tag });
        var collectionId = collection.GetProperty("collectionId").GetGuid();
        var roles = (await (await alice.GetAsync("/api/roles")).ReadJsonAsync()).EnumerateArray().ToList();
        var roleId = roles.First(r => r.GetProperty("roleCode").GetString() == "Management").GetProperty("roleId").GetGuid();
        var userId = _db.Data.UserA1Id;
        var organizationId = _db.Data.OrganizationAId;

        Probe[] tenantProbes =
        [
            new("GET", $"/api/applications/{applicationId}"), new("PUT", $"/api/applications/{applicationId}", new { applicationName = "x", applicationNameAr = "x" }), new("PUT", $"/api/applications/{applicationId}/examples", new { examples = new[] { new { en = "Hijacked" } } }), new("PUT", $"/api/applications/{applicationId}/connection", new { connectionString = source }), new("POST", $"/api/applications/{applicationId}/approval", new { isApproved = false }), new("POST", $"/api/applications/{applicationId}/test-connection", new { }),
            new("POST", $"/api/metadata/applications/{applicationId}/refresh", new { }), new("GET", $"/api/metadata/applications/{applicationId}/objects"),
            new("PUT", $"/api/departments/{departmentId}", new { departmentCode = "SW-" + tag, departmentNameEn = "Hijacked", departmentNameAr = "x" }),
            new("PUT", $"/api/sections/{sectionId}", new { departmentId, sectionCode = "SW-A", sectionNameEn = "Hijacked", sectionNameAr = "x" }),
            new("PUT", $"/api/roles/{roleId}", new { roleCode = "Management", roleNameEn = "Hijacked", roleNameAr = "x", description = "x", allowedRowScopes = new[] { "ALL" } }), new("DELETE", $"/api/roles/{roleId}"),
            new("GET", $"/api/users/{userId}"), new("PUT", $"/api/users/{userId}", new { fullName = "Hijacked", preferredLanguage = "en", roleCodes = new[] { "OrganizationAdmin" } }), new("POST", $"/api/users/{userId}/status", new { status = "Inactive" }), new("POST", $"/api/users/{userId}/reset-password", new { }),
            new("GET", $"/api/permissions/users/{userId}/effective"), new("GET", $"/api/permissions/roles/{roleId}/applications/{applicationId}"),
            new("GET", $"/api/support/tickets/{ticketId}"), new("GET", $"/api/support/tickets/{ticketId}/comments"), new("POST", $"/api/support/tickets/{ticketId}/comments", new { body = "hijack" }), new("POST", $"/api/support/tickets/{ticketId}/status", new { status = "Cancelled", note = "x" }), new("GET", $"/api/support/tickets/{ticketId}/attachments"), new("GET", $"/api/support/tickets/{ticketId}/timeline"),
            new("PUT", $"/api/history/collections/{collectionId}", new { name = "Hijacked" }), new("DELETE", $"/api/history/collections/{collectionId}"), new("POST", $"/api/history/collections/{collectionId}/items", new { historyId = Guid.NewGuid() }),
            new("GET", $"/api/history/{Guid.NewGuid()}"), new("GET", $"/api/shares/with-me/{Guid.NewGuid()}"), new("GET", $"/api/history/{Guid.NewGuid()}/shares"),
            new("GET", $"/api/insights/roles/{roleId}/metrics"), new("GET", $"/api/platform/organizations/{organizationId}"),
        ];

        // 1. A user of organization B: nothing of A is reachable, by any verb (administration routes answer 403 to a non-administrator, tenant routes 404).
        using var bob = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await bob.LoginAsync(TestData.BobEmail, TestData.UserPassword)).StatusCode);
        foreach (var probe in tenantProbes)
        {
            var response = await SendAsync(bob, probe);
            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest, $"{probe.Method} {probe.Path} answered {(int)response.StatusCode} to another organization.");
            await AssertNoOrganizationAData(response, probe);
        }

        Assert.DoesNotContain((await (await bob.GetAsync("/api/history/collections")).ReadJsonAsync()).EnumerateArray(), c => c.GetProperty("collectionId").GetGuid() == collectionId);
        Assert.DoesNotContain((await (await bob.GetAsync("/api/support/tickets?pageSize=100")).ReadJsonAsync()).GetProperty("items").EnumerateArray(), t => t.GetProperty("ticketId").GetGuid() == ticketId);
        Assert.DoesNotContain((await (await bob.GetAsync("/api/ai/applications")).ReadJsonAsync()).EnumerateArray(), a => a.GetProperty("applicationId").GetGuid() == applicationId);

        // 2. Anonymous: everything behind the session answers 401 before any lookup.
        using var anonymous = _factory.CreateClient();
        await anonymous.RefreshCsrfAsync();
        foreach (var probe in tenantProbes)
        {
            var response = await SendAsync(anonymous, probe);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{probe.Method} {probe.Path} answered {(int)response.StatusCode} anonymously.");
        }

        // 3. A platform administrator never reaches business data or organization-user features.
        using var platform = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await platform.LoginAsync(TestData.AdminEmail, TestData.AdminPassword)).StatusCode);
        Probe[] businessProbes =
        [
            new("GET", "/api/history"), new("GET", "/api/history/reports"), new("GET", "/api/shares/with-me"), new("GET", "/api/ai/applications"), new("POST", "/api/ai/ask", new { applicationId, question = "anything" }),
            new("GET", $"/api/applications/{applicationId}"), new("GET", $"/api/metadata/applications/{applicationId}/objects"), new("GET", $"/api/users/{userId}"), new("GET", "/api/permissions/me"), new("GET", $"/api/support/tickets/{ticketId}/attachments"),
            new("GET", "/api/insights/dashboard"), new("GET", "/api/organization/logo"), new("GET", "/api/organization"),
        ];
        foreach (var probe in businessProbes)
        {
            var response = await SendAsync(platform, probe);
            Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound, $"{probe.Method} {probe.Path} answered {(int)response.StatusCode} to a platform administrator.");
            await AssertNoOrganizationAData(response, probe);
        }

        // 4. An organization user without the administrator role cannot touch administration.
        var plainEmail = await InsertPlainUserAsync(organizationId);
        using var plain = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await plain.LoginAsync(plainEmail, TestData.UserPassword)).StatusCode);
        Probe[] adminProbes =
        [
            new("GET", "/api/users"), new("GET", $"/api/users/{userId}"), new("POST", "/api/users", new { email = "x@org-a.test", fullName = "x" }), new("GET", "/api/applications"), new("POST", "/api/applications/test-connection", new { databaseType = "SqlServer", connectionString = source }),
            new("GET", $"/api/metadata/applications/{applicationId}/objects"), new("GET", $"/api/permissions/roles/{roleId}/applications/{applicationId}"), new("POST", "/api/departments", new { departmentCode = "X", departmentNameEn = "x", departmentNameAr = "x" }),
            new("PUT", "/api/organization", new { organizationName = "x", organizationNameAr = "x" }), new("GET", "/api/insights/dashboard"),
        ];
        foreach (var probe in adminProbes)
        {
            var response = await SendAsync(plain, probe);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{probe.Method} {probe.Path} answered {(int)response.StatusCode} to a user without the administrator role.");
        }

        // The user-level surface still works for that user (so the 403s above are authorization, not a broken session); structure lists are read-only for every user of the organization.
        Assert.Equal(HttpStatusCode.OK, (await plain.GetAsync("/api/history")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await plain.GetAsync("/api/departments")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await plain.GetAsync("/api/permissions/me")).StatusCode);

        // 5. Secrets never reach the audit trail, usage events, notifications or the email outbox; the connection string is stored encrypted.
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            Assert.False(await context.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.DetailsJson!.Contains(secret)));
            Assert.False(await context.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.DetailsJson!.Contains("Password=")));
            Assert.False(await context.UsageEvents.IgnoreQueryFilters().AnyAsync(u => u.MetadataJson!.Contains(secret)));
            Assert.False(await context.Notifications.IgnoreQueryFilters().AnyAsync(n => n.DataJson!.Contains(secret)));
            Assert.False(await context.EmailMessages.AnyAsync(e => e.Subject.Contains(secret)));
            Assert.True(await context.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.EntityId == applicationId.ToString()), "creating an application must be audited");
            var stored = await context.Applications.IgnoreQueryFilters().SingleAsync(a => a.ApplicationId == applicationId);
            Assert.DoesNotContain(secret, stored.EncryptedConnectionString ?? string.Empty, StringComparison.Ordinal);
        }
    }

    private static async Task<System.Text.Json.JsonElement> CreatedAsync<T>(HttpClient client, string path, T body)
    {
        var response = await client.PostJsonAsync(path, body);
        Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, $"POST {path} -> {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.ReadJsonAsync();
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, Probe probe)
        => probe.Method switch
        {
            "GET" => await client.GetAsync(probe.Path),
            "POST" => await client.PostJsonAsync(probe.Path, probe.Body ?? new { }),
            "PUT" => await client.PutAsJsonAsync(new Uri(probe.Path, UriKind.Relative), probe.Body ?? new { }),
            "DELETE" => await client.DeleteAsync(new Uri(probe.Path, UriKind.Relative)),
            _ => throw new ArgumentOutOfRangeException(nameof(probe)),
        };

    private static async Task AssertNoOrganizationAData(HttpResponseMessage response, Probe probe)
    {
        var body = await response.Content.ReadAsStringAsync();
        foreach (var marker in new[] { "Sweep app", "Sweep Ops", "Sweep ticket", "Sweep collection", TestData.AliceEmail, "connectionString" })
        {
            Assert.False(body.Contains(marker, StringComparison.OrdinalIgnoreCase), $"{probe.Method} {probe.Path} leaked '{marker}': {body}");
        }
    }

    private async Task<string> InsertPlainUserAsync(Guid organizationId)
    {
        var email = $"plain.{Guid.NewGuid():N}@org-a.test";
        var userId = Core.Common.Ids.New();
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        context.OrganizationUsers.Add(TestData.NewUser(userId, organizationId, email, "Plain User"));
        context.LoginIdentities.Add(TestData.Identity(email, Core.Tenancy.PrincipalType.OrganizationUser, userId, organizationId, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
        return email;
    }
}
