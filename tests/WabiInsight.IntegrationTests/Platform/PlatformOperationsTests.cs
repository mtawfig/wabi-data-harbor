using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Platform;

[Collection(DatabaseCollectionDefinition.Name)]
public sealed class PlatformOperationsTests : IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly ApiFactory _factory;

    public PlatformOperationsTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new ApiFactory(db.ConnectionString);
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Logins_AndEmailDeliveries_ProduceUsageEvents_ThatFeedTheDashboard()
    {
        using var alice = _factory.CreateClient();
        await alice.LoginAsync(TestData.AliceEmail, TestData.UserPassword);
        using var attacker = _factory.CreateClient();
        await attacker.LoginAsync(TestData.BobEmail, "wrong-password-123456");

        using var platform = _factory.CreateClient();
        await platform.LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        var create = await platform.PostJsonAsync("/api/platform/administrators", new { email = "metrics.admin@wabitex.test", fullName = "Metrics Admin" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        await _factory.Emails.WaitForAsync("metrics.admin@wabitex.test");
        await Task.Delay(300);

        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            var types = await context.UsageEvents.AsNoTracking().Select(e => e.EventType).Distinct().ToListAsync();
            Assert.Contains(UsageEventTypes.Login, types);
            Assert.Contains(UsageEventTypes.LoginFailed, types);
            Assert.Contains(UsageEventTypes.AdministratorCreated, types);
            Assert.Contains(UsageEventTypes.EmailQueued, types);
            Assert.Contains(UsageEventTypes.EmailSent, types);

            var aliceLogin = await context.UsageEvents.AsNoTracking().Where(e => e.EventType == UsageEventTypes.Login && e.ActorId == _db.Data.UserA1Id).OrderByDescending(e => e.CreatedAt).FirstAsync();
            Assert.Equal(_db.Data.OrganizationAId, aliceLogin.OrganizationId);
            Assert.True(aliceLogin.Succeeded);

            var sent = await context.UsageEvents.AsNoTracking().Where(e => e.EventType == UsageEventTypes.EmailSent).OrderByDescending(e => e.CreatedAt).FirstAsync();
            Assert.Null(sent.OrganizationId); // platform administrator provisioning is a platform-level event
        }

        var dashboard = await (await platform.GetAsync("/api/platform/dashboard?bucket=Day")).ReadJsonAsync();
        var kpis = dashboard.GetProperty("kpis").EnumerateArray().ToDictionary(k => k.GetProperty("code").GetString()!, k => k);
        Assert.True(kpis["totalOrganizations"].GetProperty("value").GetInt64() >= 3);
        Assert.True(kpis["activeUsersInPeriod"].GetProperty("value").GetInt64() >= 1);
        Assert.True(kpis["logins"].GetProperty("value").GetInt64() >= 1);
        Assert.True(kpis["failedLogins"].GetProperty("value").GetInt64() >= 1);
        Assert.True(kpis["emailsSent"].GetProperty("value").GetInt64() >= 1);
        Assert.True(kpis["configuredApplications"].GetProperty("value").GetInt64() >= 1);
        Assert.False(kpis["aiRequests"].GetProperty("collected").GetBoolean());

        var series = dashboard.GetProperty("series").EnumerateArray().ToDictionary(s => s.GetProperty("code").GetString()!, s => s);
        Assert.True(series["logins"].GetProperty("points").EnumerateArray().Sum(p => p.GetProperty("value").GetInt64()) >= 1);

        var body = await (await platform.GetAsync("/api/platform/dashboard")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Secret HR System", body, StringComparison.Ordinal);
        var usage = dashboard.GetProperty("organizationUsage").EnumerateArray().Single(o => o.GetProperty("organizationCode").GetString() == "ORG-A");
        Assert.True(usage.GetProperty("logins").GetInt64() >= 1);
        Assert.Contains(dashboard.GetProperty("recentErrors").EnumerateArray(), e => e.GetProperty("eventType").GetString() == UsageEventTypes.LoginFailed);
    }

    [Fact]
    public async Task Dashboard_Filters_ByOrganization_AndRejectsBadPeriods()
    {
        using var bob = _factory.CreateClient();
        await bob.LoginAsync(TestData.BobEmail, TestData.UserPassword);
        using var platform = _factory.CreateClient();
        await platform.LoginAsync(TestData.AdminEmail, TestData.AdminPassword);

        var scoped = await (await platform.GetAsync($"/api/platform/dashboard?organizationId={_db.Data.OrganizationBId}&bucket=Week")).ReadJsonAsync();
        var rows = scoped.GetProperty("organizationUsage").EnumerateArray().ToList();
        Assert.Single(rows);
        Assert.Equal("ORG-B", rows[0].GetProperty("organizationCode").GetString());
        Assert.True(rows[0].GetProperty("logins").GetInt64() >= 1);

        var bad = await platform.GetAsync("/api/platform/dashboard?from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task OrganizationUsers_CannotReadPlatformDashboard_HealthOrAudit()
    {
        using var alice = _factory.CreateClient();
        await alice.LoginAsync(TestData.AliceEmail, TestData.UserPassword);

        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/platform/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/platform/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/platform/audit")).StatusCode);
    }

    [Fact]
    public async Task Health_ReportsConfigurationDatabase_EmailProvider_AndOutbox()
    {
        using var platform = _factory.CreateClient();
        await platform.LoginAsync(TestData.AdminEmail, TestData.AdminPassword);

        var report = await (await platform.GetAsync("/api/platform/health")).ReadJsonAsync();
        var checks = report.GetProperty("checks").EnumerateArray().ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("status").GetString());
        Assert.Equal("Healthy", checks["configuration-database"]);
        Assert.Equal("Healthy", checks["email-provider"]);
        Assert.Contains("email-outbox", checks.Keys);
        Assert.Contains("api", checks.Keys);
        // P13.31: the schema matches the release, and settings still at their development values are named.
        Assert.Equal("Healthy", checks["database-schema"]);
        Assert.Equal("Degraded", checks["deployment-settings"]);
        var settings = report.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "deployment-settings");
        Assert.Contains("Platform:BaseUrl", settings.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.True(report.GetProperty("checkedAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Audit_ListsAndFilters_WithoutSecrets()
    {
        using var alice = _factory.CreateClient();
        await alice.LoginAsync(TestData.AliceEmail, TestData.UserPassword);
        using var platform = _factory.CreateClient();
        await platform.LoginAsync(TestData.AdminEmail, TestData.AdminPassword);

        var page = await (await platform.GetAsync("/api/platform/audit?eventType=Auth.LoginSucceeded&pageSize=10")).ReadJsonAsync();
        Assert.True(page.GetProperty("totalCount").GetInt32() >= 2);
        Assert.All(page.GetProperty("items").EnumerateArray(), item => Assert.Equal("Auth.LoginSucceeded", item.GetProperty("eventType").GetString()));

        var byOrganization = await (await platform.GetAsync($"/api/platform/audit?organizationId={_db.Data.OrganizationAId}&actorType=OrganizationUser")).ReadJsonAsync();
        Assert.All(byOrganization.GetProperty("items").EnumerateArray(), item => Assert.Equal(_db.Data.OrganizationAId, item.GetProperty("organizationId").GetGuid()));

        var everything = await (await platform.GetAsync("/api/platform/audit?pageSize=200")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(TestData.UserPassword, everything, StringComparison.Ordinal);
        Assert.DoesNotContain(TestData.AdminPassword, everything, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordHash", everything, StringComparison.Ordinal);

        var types = await (await platform.GetAsync("/api/platform/audit/event-types")).ReadJsonAsync();
        Assert.Contains("Auth.LoginSucceeded", types.EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public async Task UsageEvents_AreAppendOnly()
    {
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        context.UsageEvents.Add(new UsageEvent { UsageEventId = Guid.NewGuid(), ActorType = Core.Tenancy.PrincipalType.PlatformAdministrator, EventType = "Test.Event", CreatedAt = DateTimeOffset.UtcNow });
        await context.SaveChangesAsync();

        var row = await context.UsageEvents.SingleAsync(e => e.EventType == "Test.Event");
        row.Quantity = 99;
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        var again = await context.UsageEvents.SingleAsync(e => e.EventType == "Test.Event");
        context.UsageEvents.Remove(again);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }
}
