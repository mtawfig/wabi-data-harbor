using System.Net;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Ai;

/// <summary>
/// P13.50, end to end against a real source table of 2,350 rows: a list longer than the rows fetched gets its count and
/// totals from the database, "View all in Excel" holds every row, and a person an answer was shared with gets the
/// whole list only when they may read the data themselves.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class LongListTests : IDisposable
{
    private const int Rows = 2350;
    private readonly SqlServerTestDatabase _db;
    private readonly AiApiFactory _factory;
    private readonly string _keyRing = Path.Combine(Path.GetTempPath(), "wi-keys-tests", "ai-long-list-tests");

    public LongListTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new AiApiFactory(db.ConnectionString, new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = _keyRing, ["Ai:Anthropic:ApiKey"] = "sk-ant-seeded-platform-key-0002" });
    }

    public void Dispose() => _factory.Dispose();

    private static string ListPlan(int? limit = null) => $$"""
        {"objective":"All entries","source":{"schema":"dbo","object":"WiLongList","alias":"l"},"joins":[],
         "select":[{"alias":"id","column":"l.Id","aggregate":null,"dateTrunc":null},{"alias":"label","column":"l.Label","aggregate":null,"dateTrunc":null},
                   {"alias":"amount","column":"l.Amount","aggregate":null,"dateTrunc":null},{"alias":"created","column":"l.CreatedOn","aggregate":null,"dateTrunc":null}],
         "filters":null,"groupBy":[],"orderBy":[{"alias":"id","direction":"asc"}],"limit":{{(limit is { } n ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")}},
         "visualization":{"type":"table","x":null,"y":[]},"assumptions":[]}
        """;

    [Fact]
    public async Task ALongList_HasItsCountAndTotalsFromTheDatabase_AndExcelHoldsEveryRow_UnderTheExportersOwnPermissions()
    {
        FakeAiProvider.Script = null;
        await RunSqlAsync($"""
            IF OBJECT_ID('dbo.WiLongList') IS NOT NULL DROP TABLE dbo.WiLongList;
            CREATE TABLE dbo.WiLongList (Id int NOT NULL PRIMARY KEY, Label nvarchar(40) NOT NULL, Amount int NOT NULL, CreatedOn datetime2 NOT NULL);
            INSERT INTO dbo.WiLongList (Id, Label, Amount, CreatedOn)
            SELECT n, CONCAT(N'Entry ', FORMAT(n, '0000')), n * 2, DATEADD(day, n % 28, '2025-03-01')
            FROM (SELECT TOP ({Rows}) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) numbers;
            """);
        try
        {
            using var alice = await LoginAsync(TestData.AliceEmail);
            var applicationId = await PrepareApplicationAsync(alice, "longlist");
            var roles = (await (await alice.GetAsync("/api/roles")).ReadJsonAsync()).EnumerateArray().ToList();
            var managementId = roles.Single(r => r.GetProperty("roleCode").GetString() == "Management").GetProperty("roleId").GetGuid();
            var (_, managerEmail) = await InsertUserAsync(_db.Data.OrganizationAId, managementId);
            var (colleagueId, colleagueEmail) = await InsertUserAsync(_db.Data.OrganizationAId, managementId);
            var (outsiderId, outsiderEmail) = await InsertUserAsync(_db.Data.OrganizationAId, null);
            using var manager = await LoginAsync(managerEmail);
            using var colleague = await LoginAsync(colleagueEmail);
            using var outsider = await LoginAsync(outsiderEmail);
            using var bob = await LoginAsync(TestData.BobEmail);

            // 1. The list is longer than the rows fetched: a thousand rows come back, and the database says how many
            //    there are and what they add up to. The totals are those of the whole list, not of the thousand.
            FakeAiProvider.Requests.Clear();
            FakeAiProvider.Script = request => request.SchemaName == "wabi_interpretation" ? """{"narrative":"Entries.","highlights":[]}""" : ListPlan();
            var asked = await manager.PostJsonAsync("/api/ai/ask", new { applicationId, question = "List every entry", language = "en", interpret = true });
            Assert.True(asked.StatusCode == HttpStatusCode.OK, await asked.Content.ReadAsStringAsync());
            var answer = await asked.ReadJsonAsync();
            var historyId = answer.GetProperty("historyId").GetGuid();
            var result = answer.GetProperty("panels")[0].GetProperty("result");
            Assert.Equal(1000, result.GetProperty("rows").GetArrayLength());
            Assert.True(result.GetProperty("truncated").GetBoolean());
            Assert.Equal(Rows, result.GetProperty("totalRows").GetInt64());
            var kpis = result.GetProperty("kpis").EnumerateArray().ToList();
            double Kpi(string column, string kind) => kpis.Single(k => k.GetProperty("column").GetString() == column && k.GetProperty("kind").GetString() == kind).GetProperty("value").GetDouble();
            Assert.Equal(Rows * (Rows + 1d), Kpi("amount", "sum"));
            Assert.Equal(2d, Kpi("amount", "min"));
            Assert.Equal(Rows * 2d, Kpi("amount", "max"));
            Assert.Equal(Rows + 1d, Kpi("amount", "avg"));

            // The explainer is given the whole list's count and figures, and is not told how many rows are displayed.
            var explain = FakeAiProvider.Requests.Last(r => r.SchemaName == "wabi_interpretation");
            Assert.Contains($"\"rowCount\":{Rows},\"complete\":false", explain.UserContent, StringComparison.Ordinal);
            Assert.Contains($"\"kind\":\"max\",\"value\":{Rows * 2}", explain.UserContent, StringComparison.Ordinal);
            Assert.Contains("Never say how many rows are displayed or were given to you", explain.SystemPrompt, StringComparison.Ordinal);

            // The count query is on the question's record with the others, and never reaches the browser.
            await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
            {
                var log = await context.AnalysisQueryLogs.IgnoreQueryFilters().SingleAsync(l => l.HistoryId == historyId);
                Assert.Contains("COUNT_BIG(*) AS [wi_rows]", log.QueriesJson, StringComparison.Ordinal);
                Assert.Contains("FROM (SELECT [l].[Id] AS [id]", log.QueriesJson, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("COUNT_BIG", await asked.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // 2. "View all in Excel": the workbook holds every row, read again from the source.
            var export = await manager.PostJsonAsync($"/api/history/{historyId}/versions/1/export/Xlsx", new { });
            Assert.True(export.StatusCode == HttpStatusCode.OK, await export.Content.ReadAsStringAsync());
            AssertWholeList(await export.Content.ReadAsByteArrayAsync());

            // PDF and Word still export, with their hundred rows.
            Assert.Equal(HttpStatusCode.OK, (await manager.PostJsonAsync($"/api/history/{historyId}/versions/1/export/Pdf", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await manager.PostJsonAsync($"/api/history/{historyId}/versions/1/export/Docx", new { })).StatusCode);

            // 3. A requested number of rows is a complete answer: nothing is counted, nothing is marked.
            FakeAiProvider.Script = _ => ListPlan(5);
            var topFive = (await (await manager.PostJsonAsync("/api/ai/ask", new { applicationId, question = "First five entries", language = "en", interpret = false })).ReadJsonAsync()).GetProperty("panels")[0].GetProperty("result");
            Assert.Equal(5, topFive.GetProperty("rows").GetArrayLength());
            Assert.False(topFive.GetProperty("truncated").GetBoolean());
            Assert.False(topFive.TryGetProperty("totalRows", out var none) && none.ValueKind != JsonValueKind.Null);

            // 4. Shared with a colleague who may read the data themselves: they are offered the whole list and get it.
            Assert.Equal(HttpStatusCode.OK, (await manager.PostJsonAsync($"/api/history/{historyId}/shares", new { recipientUserIds = new[] { colleagueId, outsiderId } })).StatusCode);
            var forColleague = await (await colleague.GetAsync($"/api/shares/with-me/{historyId}")).ReadJsonAsync();
            Assert.True(forColleague.GetProperty("canViewAllInExcel").GetBoolean());
            var colleagueExport = await colleague.PostJsonAsync($"/api/shares/with-me/{historyId}/versions/1/export/xlsx", new { });
            Assert.True(colleagueExport.StatusCode == HttpStatusCode.OK, await colleagueExport.Content.ReadAsStringAsync());
            AssertWholeList(await colleagueExport.Content.ReadAsByteArrayAsync());

            // Shared with someone who has no permission to that data: they see the answer shared with them, they are
            // not offered the whole list, and asking for it is refused without reading the source.
            var forOutsider = await (await outsider.GetAsync($"/api/shares/with-me/{historyId}")).ReadJsonAsync();
            Assert.False(forOutsider.TryGetProperty("canViewAllInExcel", out var offered) && offered.GetBoolean());
            Assert.Equal(HttpStatusCode.OK, (await outsider.GetAsync($"/api/shares/with-me/{historyId}/versions/1/result")).StatusCode);
            var refused = await outsider.PostJsonAsync($"/api/shares/with-me/{historyId}/versions/1/export/xlsx", new { });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("export_not_permitted", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // 5. Another organization reaches none of it.
            Assert.Equal(HttpStatusCode.NotFound, (await bob.PostJsonAsync($"/api/history/{historyId}/versions/1/export/Xlsx", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await bob.PostJsonAsync($"/api/shares/with-me/{historyId}/versions/1/export/xlsx", new { })).StatusCode);
        }
        finally
        {
            FakeAiProvider.Script = null;
            await RunSqlAsync("IF OBJECT_ID('dbo.WiLongList') IS NOT NULL DROP TABLE dbo.WiLongList;");
        }
    }

    /// <summary>The list's sheet: a header and every one of the rows, in order, with numbers as numbers and dates as dates.</summary>
    private static void AssertWholeList(byte[] file)
    {
        using var stream = new MemoryStream(file);
        using var workbook = new XLWorkbook(stream);
        Assert.Equal("Summary", workbook.Worksheet(1).Name);
        var sheet = workbook.Worksheet(2);
        Assert.Equal(Rows + 1, sheet.LastRowUsed()!.RowNumber());
        Assert.False(string.IsNullOrWhiteSpace(sheet.Cell(1, 1).GetString()));
        Assert.Equal(1d, sheet.Cell(2, 1).GetDouble());
        Assert.Equal("Entry 0001", sheet.Cell(2, 2).GetString());
        Assert.Equal((double)Rows, sheet.Cell(Rows + 1, 1).GetDouble());
        Assert.Equal($"Entry {Rows:0000}", sheet.Cell(Rows + 1, 2).GetString());
        Assert.Equal(Rows * 2d, sheet.Cell(Rows + 1, 3).GetDouble());
        // 2025-03-01 plus (Id mod 28) days; Id 2350 mod 28 = 26.
        Assert.Equal("2025-03-27", sheet.Cell(Rows + 1, 4).GetString());
    }

    private async Task RunSqlAsync(string sql)
    {
        await using var connection = new SqlConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<HttpClient> LoginAsync(string email, string password = TestData.UserPassword)
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, password)).StatusCode);
        return client;
    }

    private async Task<Guid> PrepareApplicationAsync(HttpClient alice, string tag)
    {
        var source = new SqlConnectionStringBuilder(_db.ConnectionString) { WorkstationID = tag }.ConnectionString;
        var created = await (await alice.PostJsonAsync("/api/applications", new { applicationCode = tag, applicationName = tag, applicationNameAr = tag, databaseType = "SqlServer", connectionString = source })).ReadJsonAsync();
        var applicationId = created.GetProperty("applicationId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/applications/{applicationId}/test-connection", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/applications/{applicationId}/approval", new { isApproved = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/metadata/applications/{applicationId}/refresh", new { })).StatusCode);
        var objects = await (await alice.GetAsync($"/api/metadata/applications/{applicationId}/objects?search=WiLongList&pageSize=10")).ReadJsonAsync();
        var objectId = objects.GetProperty("items").EnumerateArray().Single(o => o.GetProperty("objectName").GetString() == "WiLongList").GetProperty("objectId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/metadata/objects/{objectId}/approve", new { approve = true })).StatusCode);
        return applicationId;
    }

    private async Task<(Guid UserId, string Email)> InsertUserAsync(Guid organizationId, Guid? roleId)
    {
        var email = $"long.{Guid.NewGuid():N}@org.test";
        var userId = Core.Common.Ids.New();
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var now = DateTimeOffset.UtcNow;
        context.OrganizationUsers.Add(TestData.NewUser(userId, organizationId, email, "Long List User"));
        context.LoginIdentities.Add(TestData.Identity(email, Core.Tenancy.PrincipalType.OrganizationUser, userId, organizationId, now));
        if (roleId is { } r)
        {
            context.UserRoles.Add(new UserRole { OrganizationId = organizationId, UserId = userId, RoleId = r, AssignedAt = now, AssignedByAdministratorId = _db.Data.AdministratorId });
        }

        await context.SaveChangesAsync();
        return (userId, email);
    }
}
