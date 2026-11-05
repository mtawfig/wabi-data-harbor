using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Insights;

/// <summary>Platform Insights data source (specification §12): global catalog, tenant-scoped views and metric queries.</summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class PlatformInsightsTests : IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly ApiFactory _factory;

    public PlatformInsightsTests(SqlServerTestDatabase db)
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

    private static Task<HttpResponseMessage> PutJsonAsync<T>(HttpClient client, string path, T body) => client.PutAsJsonAsync(new Uri(path, UriKind.Relative), body);

    private async Task<HttpClient> AdminOfBAsync()
    {
        var email = $"insights.{Guid.NewGuid():N}@org-b.test";
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            var userId = Core.Common.Ids.New();
            var now = DateTimeOffset.UtcNow;
            context.OrganizationUsers.Add(TestData.NewUser(userId, _db.Data.OrganizationBId, email, "Insights Admin B"));
            context.LoginIdentities.Add(TestData.Identity(email, Core.Tenancy.PrincipalType.OrganizationUser, userId, _db.Data.OrganizationBId, now));
            context.UserRoles.Add(new UserRole { OrganizationId = _db.Data.OrganizationBId, UserId = userId, RoleId = _db.Data.RoleAdminBId, AssignedAt = now, AssignedByAdministratorId = _db.Data.AdministratorId });
            await context.SaveChangesAsync();
        }

        return await LoginAsync(email);
    }

    [Fact]
    public async Task Views_ExposeOnlyScopedCountsAndCodes_NeverSecretsOrText()
    {
        await using var connection = new SqlConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'insights'";
        var columns = new List<(string View, string Column)>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                columns.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        var views = columns.Select(c => c.View).Distinct().ToList();
        Assert.Equal(9, views.Count);
        Assert.All(views, v => Assert.Contains(columns, c => c.View == v && c.Column == "OrganizationId"));
        var forbidden = new[] { "PasswordHash", "SecurityStamp", "Email", "NormalizedEmail", "FullName", "Subject", "Description", "MetadataJson", "EncryptedConnectionString", "ConnectionFingerprint", "RecipientEmail", "DetailsJson", "Reason" };
        Assert.DoesNotContain(columns, c => forbidden.Contains(c.Column, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OrganizationAdministrators_QueryOnlyTheirOwnOrganization_AndCannotMoveTheScope()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        using var adminB = await AdminOfBAsync();

        var catalog = await (await alice.GetAsync("/api/insights/catalog")).ReadJsonAsync();
        Assert.Contains(catalog.GetProperty("objects").EnumerateArray(), o => o.GetProperty("viewName").GetString() == "Users");
        Assert.All(catalog.GetProperty("objects").EnumerateArray(), o => Assert.DoesNotContain(o.GetProperty("columns").EnumerateArray(), c => c.GetProperty("columnName").GetString() == "OrganizationId"));
        Assert.Contains(catalog.GetProperty("metrics").EnumerateArray(), m => m.GetProperty("metricCode").GetString() == "users.total");

        long expectedA, expectedB;
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            expectedA = await context.OrganizationUsers.IgnoreQueryFilters().CountAsync(u => u.OrganizationId == _db.Data.OrganizationAId);
            expectedB = await context.OrganizationUsers.IgnoreQueryFilters().CountAsync(u => u.OrganizationId == _db.Data.OrganizationBId);
        }

        var a = await (await alice.PostJsonAsync("/api/insights/metrics/users.total/query", new { })).ReadJsonAsync();
        var b = await (await adminB.PostJsonAsync("/api/insights/metrics/users.total/query", new { })).ReadJsonAsync();
        Assert.Equal(expectedA, (long)a.GetProperty("rows").EnumerateArray().Single().GetProperty("value").GetDouble());
        Assert.Equal(expectedB, (long)b.GetProperty("rows").EnumerateArray().Single().GetProperty("value").GetDouble());
        Assert.NotEqual(expectedA, expectedB);

        // Grouping and filtering by the organization column are refused; so are unknown columns and unapproved metrics.
        var moved = await alice.PostJsonAsync("/api/insights/metrics/users.total/query", new { groupBy = "OrganizationId" });
        Assert.Equal(HttpStatusCode.BadRequest, moved.StatusCode);
        Assert.Contains("insights_group_by_not_allowed", await moved.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var filtered = await alice.PostJsonAsync("/api/insights/metrics/users.total/query", new { filters = new[] { new { column = "OrganizationId", @operator = "eq", values = new[] { _db.Data.OrganizationBId.ToString() } } } });
        Assert.Equal(HttpStatusCode.BadRequest, filtered.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PostJsonAsync("/api/insights/metrics/does.not.exist/query", new { })).StatusCode);

        // Period, bucket and grouping over the usage view: logins of A only.
        var since = DateTimeOffset.UtcNow.AddDays(-1);
        var logins = await (await alice.PostJsonAsync("/api/insights/metrics/logins.total/query", new { from = since, bucket = "Day", groupBy = "EventType" })).ReadJsonAsync();
        var rows = logins.GetProperty("rows").EnumerateArray().ToList();
        Assert.All(rows, r => Assert.Equal("Auth.Login", r.GetProperty("group").GetString()));
        Assert.True(rows.Sum(r => r.GetProperty("value").GetDouble()) >= 1);

        // Platform administrators and plain users have no organization insights; platform administrators own the catalog instead.
        using var platform = await LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.PostJsonAsync("/api/insights/metrics/users.total/query", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync("/api/insights/catalog")).StatusCode);
        using var bob = await LoginAsync(TestData.BobEmail);
        Assert.Empty((await (await bob.GetAsync("/api/insights/catalog")).ReadJsonAsync()).GetProperty("metrics").EnumerateArray()); // no role grants
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/platform/insights-catalog")).StatusCode);
    }

    [Fact]
    public async Task PlatformAdministrators_MaintainTheGlobalCatalog_WithoutExecutingQueries()
    {
        using var platform = await LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        var catalog = await (await platform.GetAsync("/api/platform/insights-catalog")).ReadJsonAsync();
        var source = catalog.GetProperty("source");
        Assert.True(source.GetProperty("reachable").GetBoolean());
        Assert.False(source.GetProperty("dedicatedLoginConfigured").GetBoolean()); // tests run on the sa connection
        Assert.Contains("insights_login_not_configured", source.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()));
        Assert.Equal(9, source.GetProperty("viewCount").GetInt32());
        var users = catalog.GetProperty("objects").EnumerateArray().Single(o => o.GetProperty("viewName").GetString() == "Users");
        Assert.True(users.GetProperty("isApproved").GetBoolean());
        Assert.Equal("المستخدمون", users.GetProperty("displayNameAr").GetString());

        // Sync is idempotent on a fresh database.
        var sync = await (await platform.PostJsonAsync("/api/platform/insights-catalog/sync", new { })).ReadJsonAsync();
        Assert.Equal(0, sync.GetProperty("objectsAdded").GetInt32());
        Assert.Equal(0, sync.GetProperty("columnsAdded").GetInt32());

        // Column approval controls what organizations may address.
        var status = users.GetProperty("columns").EnumerateArray().Single(c => c.GetProperty("columnName").GetString() == "Status");
        var statusId = status.GetProperty("columnId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await PutJsonAsync(platform, $"/api/platform/insights-catalog/columns/{statusId}", new { displayNameEn = "Status", displayNameAr = "الحالة", isApproved = false })).StatusCode);
        using var alice = await LoginAsync(TestData.AliceEmail);
        var denied = await alice.PostJsonAsync("/api/insights/metrics/users.total/query", new { groupBy = "Status" });
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        var activeMetric = await alice.PostJsonAsync("/api/insights/metrics/users.active/query", new { });
        Assert.Equal(HttpStatusCode.BadRequest, activeMetric.StatusCode); // its fixed filter uses the unapproved column
        Assert.Equal(HttpStatusCode.OK, (await PutJsonAsync(platform, $"/api/platform/insights-catalog/columns/{statusId}", new { displayNameEn = "Status", displayNameAr = "الحالة", isApproved = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync("/api/insights/metrics/users.total/query", new { groupBy = "Status" })).StatusCode);

        // Metric definitions are validated against the view and previewed as SQL with the tenant placeholder.
        var objectId = users.GetProperty("objectId").GetGuid();
        var code = $"test.metric{Guid.NewGuid():N}"[..20];
        var bad = await platform.PostJsonAsync("/api/platform/insights-catalog/metrics", new { metricCode = code, objectId, displayNameEn = "Bad", displayNameAr = "سيئ", aggregate = "Sum", columnName = "PasswordHash" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var create = await platform.PostJsonAsync("/api/platform/insights-catalog/metrics", new { metricCode = code, objectId, displayNameEn = "Arabic users", displayNameAr = "مستخدمو العربية", aggregate = "Count", dateColumn = "CreatedAt", filters = new[] { new { column = "PreferredLanguage", @operator = "eq", values = new[] { "ar" } } }, isApproved = false });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var metricId = (await create.ReadJsonAsync()).GetProperty("metricId").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await platform.PostJsonAsync("/api/platform/insights-catalog/metrics", new { metricCode = code, objectId, displayNameEn = "Dup", displayNameAr = "مكرر", aggregate = "Count" })).StatusCode);

        var preview = await (await platform.PostJsonAsync($"/api/platform/insights-catalog/metrics/{metricId}/sql", new { bucket = "Month" })).ReadJsonAsync();
        var sql = preview.GetProperty("sql").GetString()!;
        Assert.StartsWith("SELECT DATEFROMPARTS(YEAR([CreatedAt]), MONTH([CreatedAt]), 1) AS [Bucket], COUNT_BIG(*) AS [Value] FROM [insights].[Users] WHERE [OrganizationId] = @tenant", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ar'", sql, StringComparison.Ordinal);
        Assert.Equal("<organization of the signed-in user>", preview.GetProperty("parameters").GetProperty("@tenant").GetString());

        // Unapproved metrics are invisible to organizations until approved; then they run scoped.
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PostJsonAsync($"/api/insights/metrics/{code}/query", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PutJsonAsync(platform, $"/api/platform/insights-catalog/metrics/{metricId}", new { metricCode = code, objectId, displayNameEn = "Arabic users", displayNameAr = "مستخدمو العربية", aggregate = "Count", dateColumn = "CreatedAt", filters = new[] { new { column = "PreferredLanguage", @operator = "eq", values = new[] { "ar" } } }, isApproved = true })).StatusCode);
        var result = await (await alice.PostJsonAsync($"/api/insights/metrics/{code}/query", new { })).ReadJsonAsync();
        Assert.Equal(code, result.GetProperty("metricCode").GetString());
        Assert.Single(result.GetProperty("rows").EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await platform.DeleteAsync(new Uri($"/api/platform/insights-catalog/metrics/{metricId}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PostJsonAsync($"/api/insights/metrics/{code}/query", new { })).StatusCode);

        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        Assert.True(await context.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.EventType == "Insights.MetricChanged"));
        Assert.True(await context.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.EventType == "Insights.CatalogSynced"));
        Assert.True(await context.UsageEvents.AnyAsync(u => u.EventType == "Insights.Queried" && u.OrganizationId == _db.Data.OrganizationAId));
    }
}
