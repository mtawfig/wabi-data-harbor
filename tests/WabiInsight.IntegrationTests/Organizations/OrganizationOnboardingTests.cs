using System.Net.Http.Json;
using System.Net;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;
using WabiInsight.IntegrationTests.Email;

namespace WabiInsight.IntegrationTests.Organizations;

[Collection(DatabaseCollectionDefinition.Name)]
public sealed class OrganizationOnboardingTests : IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly ApiFactory _factory;

    public OrganizationOnboardingTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new ApiFactory(db.ConnectionString);
    }

    public void Dispose() => _factory.Dispose();

    private static object NewOrganization(string code, string adminEmail, string language = "en") => new
    {
        organizationCode = code,
        organizationName = $"Organization {code}",
        organizationNameAr = $"مؤسسة {code}",
        contactEmail = $"contact@{code.ToLowerInvariant()}.test",
        countryCode = "AE",
        timeZone = "Asia/Dubai",
        defaultLanguage = language,
        initialAdministratorEmail = adminEmail,
        initialAdministratorFullName = "First Admin",
    };

    [Fact]
    public async Task PlatformAdministrator_OnboardsOrganization_WithRolesBuiltInAppAndEmailedAdministrator()
    {
        using var platform = _factory.CreateClient();
        await platform.LoginAsync(TestData.AdminEmail, TestData.AdminPassword);

        var create = await platform.PostJsonAsync("/api/platform/organizations", NewOrganization("ACME", "admin@acme.test", "ar"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var detail = await create.ReadJsonAsync();
        var organizationId = detail.GetProperty("organizationId").GetGuid();
        Assert.Equal("ACME", detail.GetProperty("organizationCode").GetString());
        Assert.Equal("Active", detail.GetProperty("status").GetString());
        Assert.Equal(1, detail.GetProperty("userCount").GetInt32());
        Assert.Equal(1, detail.GetProperty("applicationCount").GetInt32());
        Assert.Equal("admin@acme.test", detail.GetProperty("initialAdministratorEmail").GetString());
        Assert.False(detail.GetProperty("initialAdministratorActivated").GetBoolean());
        Assert.Equal("a***n@acme.test", create.Headers.GetValues("X-Delivered-To").Single());

        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            var roles = await context.Roles.IgnoreQueryFilters().Where(r => r.OrganizationId == organizationId).Select(r => r.RoleCode).OrderBy(c => c).ToListAsync();
            // Other tests may add platform templates (e.g. Auditor); the three built-in templates are always provisioned.
            Assert.Superset(new HashSet<string> { "Analyst", "Management", "OrganizationAdmin" }, roles.ToHashSet());
            var app = await context.Applications.IgnoreQueryFilters().SingleAsync(a => a.OrganizationId == organizationId);
            Assert.True(app.IsBuiltIn);
            Assert.Equal("WI-INSIGHTS", app.BuiltInCode);
            Assert.Equal(DatabaseType.PlatformInsights, app.DatabaseType);
            Assert.Null(app.EncryptedConnectionString);
        }

        // The administrator receives the temporary password (Arabic, the organization default) and is forced to change it.
        var email = await _factory.Emails.WaitForAsync("admin@acme.test");
        Assert.Equal("ar", email.Language);
        var temporary = RecordingEmailSender.TemporaryPasswordOf(email);

        using var admin = _factory.CreateClient();
        var login = await admin.LoginAsync("admin@acme.test", temporary);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = await login.ReadJsonAsync();
        Assert.True(session.GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(organizationId, session.GetProperty("organizationId").GetGuid());
        Assert.Contains("OrganizationAdmin", session.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/organization")).StatusCode);
        var change = await admin.PostJsonAsync("/api/auth/change-password", new { currentPassword = temporary, newPassword = "Acme#Admin#Strong#2026" });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        var profile = await (await admin.GetAsync("/api/organization")).ReadJsonAsync();
        Assert.Equal("ACME", profile.GetProperty("organizationCode").GetString());

        var updated = await admin.PostJsonAsync("/api/organization", new { });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, updated.StatusCode);
        var put = await PutJsonAsync(admin, "/api/organization", new { organizationName = "ACME Holdings", organizationNameAr = "أكمي القابضة", timeZone = "Asia/Riyadh", defaultLanguage = "en" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal("ACME Holdings", (await put.ReadJsonAsync()).GetProperty("organizationName").GetString());

        var after = await (await platform.GetAsync($"/api/platform/organizations/{organizationId}")).ReadJsonAsync();
        Assert.True(after.GetProperty("initialAdministratorActivated").GetBoolean());
        Assert.Equal("ACME Holdings", after.GetProperty("organizationName").GetString());
    }

    [Fact]
    public async Task Create_RejectsDuplicateCode_InvalidCode_AndRegisteredEmail_WithoutPartialRows()
    {
        using var platform = _factory.CreateClient();
        await platform.LoginAsync(TestData.AdminEmail, TestData.AdminPassword);

        var duplicateCode = await platform.PostJsonAsync("/api/platform/organizations", NewOrganization("ORG-A", "someone@new.test"));
        var invalidCode = await platform.PostJsonAsync("/api/platform/organizations", NewOrganization("bad code!", "someone@new.test"));
        var takenEmail = await platform.PostJsonAsync("/api/platform/organizations", NewOrganization("NEWCO", TestData.BobEmail));
        var badTimeZone = await platform.PostJsonAsync("/api/platform/organizations", new { organizationCode = "TZ1", organizationName = "x", organizationNameAr = "x", timeZone = "Mars/Olympus", defaultLanguage = "en", initialAdministratorEmail = "tz@new.test", initialAdministratorFullName = "T" });

        Assert.Equal(HttpStatusCode.Conflict, duplicateCode.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidCode.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, takenEmail.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badTimeZone.StatusCode);
        Assert.Contains("time_zone_invalid", await badTimeZone.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        Assert.False(await context.Organizations.AnyAsync(o => o.OrganizationCode == "NEWCO" || o.OrganizationCode == "TZ1"));
    }

    [Fact]
    public async Task Suspension_EndsSessions_BlocksLogin_AndReactivationRestoresAccess()
    {
        using var platform = _factory.CreateClient();
        await platform.LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        var create = await platform.PostJsonAsync("/api/platform/organizations", NewOrganization("SUSP", "admin@susp.test"));
        var organizationId = (await create.ReadJsonAsync()).GetProperty("organizationId").GetGuid();
        var temporary = RecordingEmailSender.TemporaryPasswordOf(await _factory.Emails.WaitForAsync("admin@susp.test"));

        using var user = _factory.CreateClient();
        await user.LoginAsync("admin@susp.test", temporary);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/auth/me")).StatusCode);

        var suspend = await platform.PostJsonAsync($"/api/platform/organizations/{organizationId}/status", new { status = "Suspended", reason = "Unpaid invoice" });
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);
        Assert.Equal("Suspended", (await suspend.ReadJsonAsync()).GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/auth/me")).StatusCode);
        var denied = await user.LoginAsync("admin@susp.test", temporary);
        Assert.EndsWith("/organization_suspended", (await denied.ReadJsonAsync()).GetProperty("type").GetString(), StringComparison.Ordinal);

        var invalid = await platform.PostJsonAsync($"/api/platform/organizations/{organizationId}/status", new { status = "Suspended" });
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);

        var reactivate = await platform.PostJsonAsync($"/api/platform/organizations/{organizationId}/status", new { status = "Active" });
        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.LoginAsync("admin@susp.test", temporary)).StatusCode);
    }

    [Fact]
    public async Task ResendInitialAdministrator_WorksUntilFirstSignInCompletes()
    {
        using var platform = _factory.CreateClient();
        await platform.LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        var create = await platform.PostJsonAsync("/api/platform/organizations", NewOrganization("RSND", "admin@rsnd.test"));
        var organizationId = (await create.ReadJsonAsync()).GetProperty("organizationId").GetGuid();
        var first = RecordingEmailSender.TemporaryPasswordOf(await _factory.Emails.WaitForAsync("admin@rsnd.test"));

        var resend = await platform.PostJsonAsync($"/api/platform/organizations/{organizationId}/initial-administrator/resend", new { });
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        await Task.Delay(300);
        var second = _factory.Emails.Messages.Where(m => m.ToEmail == "admin@rsnd.test").Select(RecordingEmailSender.TemporaryPasswordOf).Last();
        Assert.NotEqual(first, second);

        using var admin = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.LoginAsync("admin@rsnd.test", first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.LoginAsync("admin@rsnd.test", second)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostJsonAsync("/api/auth/change-password", new { currentPassword = second, newPassword = "Rsnd#Admin#Strong#2026" })).StatusCode);

        var afterActivation = await platform.PostJsonAsync($"/api/platform/organizations/{organizationId}/initial-administrator/resend", new { });
        Assert.Equal(HttpStatusCode.Conflict, afterActivation.StatusCode);
    }

    [Fact]
    public async Task PlatformList_ShowsCountsOnly_NeverApplicationNames()
    {
        using var platform = _factory.CreateClient();
        await platform.LoginAsync(TestData.AdminEmail, TestData.AdminPassword);

        var response = await platform.GetAsync("/api/platform/organizations?search=ORG-A&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var list = await response.ReadJsonAsync();

        var orgA = list.GetProperty("items").EnumerateArray().Single(o => o.GetProperty("organizationCode").GetString() == "ORG-A");
        Assert.Equal(1, orgA.GetProperty("applicationCount").GetInt32());
        Assert.DoesNotContain("Secret HR System", body, StringComparison.Ordinal);
        Assert.DoesNotContain("applicationName", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionString", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OrganizationUsers_CannotReachPlatformOrganizationEndpoints_AndSeeOnlyTheirOwnOrganization()
    {
        using var alice = _factory.CreateClient();
        await alice.LoginAsync(TestData.AliceEmail, TestData.UserPassword);
        using var bob = _factory.CreateClient();
        await bob.LoginAsync(TestData.BobEmail, TestData.UserPassword);

        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/platform/organizations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PostJsonAsync("/api/platform/organizations", NewOrganization("EVIL", "evil@x.test"))).StatusCode);

        var aliceOrg = await (await alice.GetAsync("/api/organization")).ReadJsonAsync();
        var bobOrg = await (await bob.GetAsync("/api/organization")).ReadJsonAsync();
        Assert.Equal(_db.Data.OrganizationAId, aliceOrg.GetProperty("organizationId").GetGuid());
        Assert.Equal(_db.Data.OrganizationBId, bobOrg.GetProperty("organizationId").GetGuid());

        // Alice is OrganizationAdmin of A and may edit A; Bob holds no role and may not edit B.
        var payload = new { organizationName = "Renamed", organizationNameAr = "معاد تسميتها", timeZone = "UTC", defaultLanguage = "en" };
        Assert.Equal(HttpStatusCode.OK, (await PutJsonAsync(alice, "/api/organization", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutJsonAsync(bob, "/api/organization", payload)).StatusCode);

        var bobOrgAfter = await (await bob.GetAsync("/api/organization")).ReadJsonAsync();
        Assert.Equal("Organization B", bobOrgAfter.GetProperty("organizationName").GetString());
    }

    [Fact]
    public async Task Database_RejectsRoleAssignmentAcrossOrganizations()
    {
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        context.UserRoles.Add(new UserRole { OrganizationId = _db.Data.OrganizationAId, UserId = _db.Data.UserB1Id, RoleId = _db.Data.RoleAdminAId, AssignedAt = DateTimeOffset.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        context.UserRoles.Add(new UserRole { OrganizationId = _db.Data.OrganizationAId, UserId = _db.Data.UserA1Id, RoleId = _db.Data.RoleAdminBId, AssignedAt = DateTimeOffset.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private static Task<HttpResponseMessage> PutJsonAsync<T>(HttpClient client, string path, T body)
        => client.PutAsJsonAsync(new Uri(path, UriKind.Relative), body);
}
