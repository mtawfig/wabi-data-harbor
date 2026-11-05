using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Insights;

/// <summary>Organization dashboard and per-role metric grants (specification §12.2, §14).</summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class OrganizationDashboardTests : IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly ApiFactory _factory;

    public OrganizationDashboardTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new ApiFactory(db.ConnectionString);
    }

    public void Dispose() => _factory.Dispose();

    private async Task<HttpClient> LoginAsync(string email, string password = TestData.UserPassword)
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, password)).StatusCode);
        return client;
    }

    private async Task<(Guid UserId, string Email)> InsertUserAsync(Guid organizationId, Guid? roleId)
    {
        var email = $"dash.{Guid.NewGuid():N}@org.test";
        var userId = Core.Common.Ids.New();
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var now = DateTimeOffset.UtcNow;
        context.OrganizationUsers.Add(TestData.NewUser(userId, organizationId, email, "Dash User"));
        context.LoginIdentities.Add(TestData.Identity(email, Core.Tenancy.PrincipalType.OrganizationUser, userId, organizationId, now));
        if (roleId is { } r)
        {
            context.UserRoles.Add(new UserRole { OrganizationId = organizationId, UserId = userId, RoleId = r, AssignedAt = now, AssignedByAdministratorId = _db.Data.AdministratorId });
        }

        await context.SaveChangesAsync();
        return (userId, email);
    }

    [Fact]
    public async Task Dashboard_ShowsOnlyTheAdministratorsOrganization()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        var (_, adminBEmail) = await InsertUserAsync(_db.Data.OrganizationBId, _db.Data.RoleAdminBId);
        using var adminB = await LoginAsync(adminBEmail);

        var a = await (await alice.GetAsync("/api/insights/dashboard?bucket=Day")).ReadJsonAsync();
        var b = await (await adminB.GetAsync("/api/insights/dashboard?bucket=Day")).ReadJsonAsync();
        long Kpi(JsonElement d, string code) => d.GetProperty("kpis").EnumerateArray().Single(k => k.GetProperty("code").GetString() == code).GetProperty("value").GetInt64();
        long usersA, usersB;
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            usersA = await context.OrganizationUsers.IgnoreQueryFilters().CountAsync(u => u.OrganizationId == _db.Data.OrganizationAId);
            usersB = await context.OrganizationUsers.IgnoreQueryFilters().CountAsync(u => u.OrganizationId == _db.Data.OrganizationBId);
        }

        Assert.Equal(usersA, Kpi(a, "totalUsers"));
        Assert.Equal(usersB, Kpi(b, "totalUsers"));
        Assert.True(Kpi(a, "logins") >= 1);
        Assert.Contains(a.GetProperty("kpis").EnumerateArray(), k => k.GetProperty("code").GetString() == "analysesCompleted" && !k.GetProperty("collected").GetBoolean());
        Assert.Contains(a.GetProperty("series").EnumerateArray(), s => s.GetProperty("code").GetString() == "logins" && s.GetProperty("points").GetArrayLength() >= 1);
        // Applications table shows names (allowed on the organization side) but never connection details.
        var body = await (await alice.GetAsync("/api/insights/dashboard")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("connectionString", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", body, StringComparison.Ordinal);
        Assert.All(a.GetProperty("applications").EnumerateArray(), app => Assert.False(string.IsNullOrEmpty(app.GetProperty("applicationCode").GetString())));

        // Plain users and platform administrators have no dashboard.
        using var bob = await LoginAsync(TestData.BobEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/insights/dashboard")).StatusCode);
        using var platform = await LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync("/api/insights/dashboard")).StatusCode);
    }

    [Fact]
    public async Task MetricGrants_GiveRolesSpecificMetrics_AndNeverCrossOrganizations()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        var roles = (await (await alice.GetAsync("/api/roles")).ReadJsonAsync()).EnumerateArray().ToList();
        var analystId = roles.Single(r => r.GetProperty("roleCode").GetString() == "Analyst").GetProperty("roleId").GetGuid();
        var orgAdminId = roles.Single(r => r.GetProperty("roleCode").GetString() == "OrganizationAdmin").GetProperty("roleId").GetGuid();
        var (_, analystEmail) = await InsertUserAsync(_db.Data.OrganizationAId, analystId);
        using var analyst = await LoginAsync(analystEmail);

        // Nothing granted: empty catalog, metric not found; the organization administrator sees everything approved.
        Assert.Empty((await (await analyst.GetAsync("/api/insights/catalog")).ReadJsonAsync()).GetProperty("metrics").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await analyst.PostJsonAsync("/api/insights/metrics/users.total/query", new { })).StatusCode);
        var grants = await (await alice.GetAsync($"/api/insights/roles/{analystId}/metrics")).ReadJsonAsync();
        Assert.False(grants.GetProperty("isAutomatic").GetBoolean());
        Assert.Empty(grants.GetProperty("grantedMetricIds").EnumerateArray());
        var usersTotal = grants.GetProperty("metrics").EnumerateArray().Single(m => m.GetProperty("metricCode").GetString() == "users.total").GetProperty("metricId").GetGuid();
        var automatic = await (await alice.GetAsync($"/api/insights/roles/{orgAdminId}/metrics")).ReadJsonAsync();
        Assert.True(automatic.GetProperty("isAutomatic").GetBoolean());
        Assert.Equal(automatic.GetProperty("metrics").GetArrayLength(), automatic.GetProperty("grantedMetricIds").GetArrayLength());
        var locked = await alice.PutAsJsonAsync(new Uri($"/api/insights/roles/{orgAdminId}/metrics", UriKind.Relative), new { metricIds = new[] { usersTotal } });
        Assert.Equal(HttpStatusCode.BadRequest, locked.StatusCode);
        Assert.Contains("organization_admin_metrics_automatic", await locked.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Grant one metric: the analyst can run that one and nothing else; unknown ids are rejected.
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PutAsJsonAsync(new Uri($"/api/insights/roles/{analystId}/metrics", UriKind.Relative), new { metricIds = new[] { Guid.NewGuid() } })).StatusCode);
        var set = await alice.PutAsJsonAsync(new Uri($"/api/insights/roles/{analystId}/metrics", UriKind.Relative), new { metricIds = new[] { usersTotal } });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Single((await set.ReadJsonAsync()).GetProperty("grantedMetricIds").EnumerateArray());
        var catalog = await (await analyst.GetAsync("/api/insights/catalog")).ReadJsonAsync();
        Assert.Equal("users.total", catalog.GetProperty("metrics").EnumerateArray().Single().GetProperty("metricCode").GetString());
        Assert.Equal(HttpStatusCode.OK, (await analyst.PostJsonAsync("/api/insights/metrics/users.total/query", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await analyst.PostJsonAsync("/api/insights/metrics/logins.total/query", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.GetAsync($"/api/insights/roles/{analystId}/metrics")).StatusCode);

        // Organization B's administrator cannot read or write A's role grants.
        var (_, adminBEmail) = await InsertUserAsync(_db.Data.OrganizationBId, _db.Data.RoleAdminBId);
        using var adminB = await LoginAsync(adminBEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.GetAsync($"/api/insights/roles/{analystId}/metrics")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.PutAsJsonAsync(new Uri($"/api/insights/roles/{analystId}/metrics", UriKind.Relative), new { metricIds = new[] { usersTotal } })).StatusCode);

        // Revoke: empty set removes the rows and the analyst loses the metric.
        Assert.Equal(HttpStatusCode.OK, (await alice.PutAsJsonAsync(new Uri($"/api/insights/roles/{analystId}/metrics", UriKind.Relative), new { metricIds = Array.Empty<Guid>() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await analyst.PostJsonAsync("/api/insights/metrics/users.total/query", new { })).StatusCode);
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        Assert.False(await context.RoleMetricGrants.IgnoreQueryFilters().AnyAsync(g => g.RoleId == analystId));
        Assert.True(await context.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.EventType == "Insights.MetricGrantChanged" && a.EntityId == analystId.ToString()));
    }
}
