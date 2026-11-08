using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Ai;

/// <summary>Persistent history (specification §23): every ask is saved, reopening never re-queries, re-runs are versions, everything is owner-scoped.</summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class HistoryTests : IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly AiApiFactory _factory;
    private readonly string _keyRing = Path.Combine(Path.GetTempPath(), "wi-keys-tests", "ai-history-tests");

    public HistoryTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new AiApiFactory(db.ConnectionString, new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = _keyRing, ["Ai:Anthropic:ApiKey"] = "sk-ant-seeded-platform-key-0003" });
    }

    public void Dispose() => _factory.Dispose();

    private async Task<HttpClient> LoginAsync(string email, string password = TestData.UserPassword)
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, password)).StatusCode);
        return client;
    }

    private const string TicketsPlan = """
        {"objective":"Tickets by status","assumptions":[],"deliverables":[],"queries":[
          {"title":"Total","source":{"schema":"dbo","object":"SupportTickets","alias":"t"},"joins":[],"select":[{"alias":"tickets","column":null,"aggregate":"count","dateTrunc":null}],"filters":null,"groupBy":[],"orderBy":[],"limit":null,"visualization":{"type":"kpi","x":null,"y":["tickets"]}},
          {"title":"By status","source":{"schema":"dbo","object":"SupportTickets","alias":"t"},"joins":[],"select":[{"alias":"status","column":"t.Status","aggregate":null,"dateTrunc":null},{"alias":"tickets","column":null,"aggregate":"count","dateTrunc":null}],"filters":null,"groupBy":["status"],"orderBy":[{"alias":"tickets","direction":"desc"}],"limit":null,"visualization":{"type":"bar","x":"status","y":["tickets"]}}]}
        """;

    [Fact]
    public async Task Ask_IsSavedToHistory_ReopenNeverRequeries_RerunAddsVersions_AndEverythingIsOwnerScoped()
    {
        FakeAiProvider.Script = null;
        try
        {
            using var alice = await LoginAsync(TestData.AliceEmail);
            var source = new SqlConnectionStringBuilder(_db.ConnectionString) { WorkstationID = "aihistory" }.ConnectionString;
            var created = await (await alice.PostJsonAsync("/api/applications", new { applicationCode = "aihistory", applicationName = "History app", applicationNameAr = "تطبيق", databaseType = "SqlServer", connectionString = source })).ReadJsonAsync();
            var applicationId = created.GetProperty("applicationId").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/applications/{applicationId}/test-connection", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/applications/{applicationId}/approval", new { isApproved = true })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/metadata/applications/{applicationId}/refresh", new { })).StatusCode);
            var objects = await (await alice.GetAsync($"/api/metadata/applications/{applicationId}/objects?search=SupportTickets&pageSize=10")).ReadJsonAsync();
            var objectId = objects.GetProperty("items").EnumerateArray().Single(o => o.GetProperty("schemaName").GetString() == "dbo" && o.GetProperty("objectName").GetString() == "SupportTickets").GetProperty("objectId").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/metadata/objects/{objectId}/approve", new { approve = true })).StatusCode);
            var roles = (await (await alice.GetAsync("/api/roles")).ReadJsonAsync()).EnumerateArray().ToList();
            var managementId = roles.Single(r => r.GetProperty("roleCode").GetString() == "Management").GetProperty("roleId").GetGuid();
            var (ownerId, ownerEmail) = await InsertUserAsync(_db.Data.OrganizationAId, managementId);
            var (_, otherEmail) = await InsertUserAsync(_db.Data.OrganizationAId, managementId);
            using var owner = await LoginAsync(ownerEmail);
            using var other = await LoginAsync(otherEmail);
            using var bob = await LoginAsync(TestData.BobEmail);

            FakeAiProvider.Script = (request) => request.SchemaName == "wabi_interpretation" ? """{"narrative":"Stored narrative.","highlights":["h1"]}""" : TicketsPlan;
            var ask = await owner.PostJsonAsync("/api/ai/ask", new { applicationId, question = "How many tickets per status?", language = "en" });
            Assert.True(ask.StatusCode == HttpStatusCode.OK, await ask.Content.ReadAsStringAsync());
            var answer = await ask.ReadJsonAsync();
            var historyId = answer.GetProperty("historyId").GetGuid();
            Assert.Equal(1, answer.GetProperty("versionNumber").GetInt32());

            // Listed for the owner with its title, type and version; invisible to another user and to another organization.
            var list = await (await owner.GetAsync("/api/history?search=tickets")).ReadJsonAsync();
            var row = Assert.Single(list.GetProperty("items").EnumerateArray());
            Assert.Equal(historyId, row.GetProperty("historyId").GetGuid());
            Assert.Equal("Tickets by status", row.GetProperty("title").GetString());
            Assert.Equal("Dashboard", row.GetProperty("analysisType").GetString());
            Assert.Equal(1, row.GetProperty("currentVersionNumber").GetInt32());
            Assert.Empty((await (await other.GetAsync("/api/history")).ReadJsonAsync()).GetProperty("items").EnumerateArray());
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/history/{historyId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/history/{historyId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/history/{historyId}/versions/1/result")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.PostJsonAsync($"/api/history/{historyId}/rerun", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync(new Uri($"/api/history/{historyId}", UriKind.Relative))).StatusCode);

            // Reopening returns the stored answer and asks the model nothing.
            var requestsBefore = FakeAiProvider.Requests.Count;
            var stored = await (await owner.GetAsync($"/api/history/{historyId}/versions/1/result")).ReadJsonAsync();
            Assert.Equal(historyId, stored.GetProperty("historyId").GetGuid());
            Assert.Equal("Stored narrative.", stored.GetProperty("interpretation").GetProperty("narrative").GetString());
            Assert.Equal(answer.GetProperty("panels")[1].GetProperty("result").GetProperty("rows").GetRawText(), stored.GetProperty("panels")[1].GetProperty("result").GetProperty("rows").GetRawText());
            // The stored answer carries the plan and display labels, never the SQL (kept in AnalysisQueryLogs only).
            Assert.All(stored.GetProperty("panels").EnumerateArray(), p => Assert.False(p.TryGetProperty("compiledSqlPreview", out _)));
            Assert.Equal(requestsBefore, FakeAiProvider.Requests.Count);
            await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
            {
                Assert.Equal(1, await context.AnalysisExecutionMetrics.IgnoreQueryFilters().CountAsync(m => m.UserId == ownerId));
                var version = await context.AnalysisHistoryVersions.IgnoreQueryFilters().SingleAsync(v => v.HistoryId == historyId);
                Assert.Contains("[dbo].[SupportTickets]", version.CompiledSqlPreview, StringComparison.Ordinal);
                Assert.Equal(2, version.PanelCount);
                Assert.Equal(1, await context.AnalysisArtifacts.IgnoreQueryFilters().CountAsync(a => a.HistoryId == historyId));
            }

            // Re-run: a new version under current permissions; the first version is untouched.
            Assert.Equal(HttpStatusCode.Created, (await owner.PostJsonAsync("/api/support/tickets", new { ticketType = "Request", impact = "Low", subject = "History seed", description = "Seed ticket for the history test.", requiresSupportAccess = false })).StatusCode);
            var rerun = await owner.PostJsonAsync($"/api/history/{historyId}/rerun", new { });
            Assert.True(rerun.StatusCode == HttpStatusCode.OK, await rerun.Content.ReadAsStringAsync());
            var second = await rerun.ReadJsonAsync();
            Assert.Equal(2, second.GetProperty("versionNumber").GetInt32());
            Assert.Equal(historyId, second.GetProperty("historyId").GetGuid());
            var detail = await (await owner.GetAsync($"/api/history/{historyId}")).ReadJsonAsync();
            Assert.Equal(2, detail.GetProperty("summary").GetProperty("currentVersionNumber").GetInt32());
            Assert.Equal(2, detail.GetProperty("versions").GetArrayLength());
            var firstAgain = await (await owner.GetAsync($"/api/history/{historyId}/versions/1/result")).ReadJsonAsync();
            Assert.Equal(stored.GetProperty("panels")[0].GetProperty("result").GetProperty("rows").GetRawText(), firstAgain.GetProperty("panels")[0].GetProperty("result").GetProperty("rows").GetRawText());
            Assert.Equal(stored.GetProperty("panels")[0].GetProperty("result").GetProperty("rows")[0][0].GetInt64() + 1, second.GetProperty("panels")[0].GetProperty("result").GetProperty("rows")[0][0].GetInt64());

            // A rejected re-run is recorded as a failed version and the current version number moves on.
            FakeAiProvider.Script = (_) => TicketsPlan.Replace("\"t.Status\"", "\"t.NoSuchColumn\"", StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.PostJsonAsync($"/api/history/{historyId}/rerun", new { })).StatusCode);
            detail = await (await owner.GetAsync($"/api/history/{historyId}")).ReadJsonAsync();
            Assert.Equal("Failed", detail.GetProperty("summary").GetProperty("status").GetString());
            var failed = detail.GetProperty("versions").EnumerateArray().First();
            Assert.Equal(3, failed.GetProperty("versionNumber").GetInt32());
            Assert.Equal("Failed", failed.GetProperty("status").GetString());
            Assert.False(failed.GetProperty("hasResult").GetBoolean());
            Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/history/{historyId}/versions/3/result")).StatusCode);

            // Exports: PDF, DOCX and XLSX of a version land in the reports library, downloadable by the owner only, with a generation event each.
            foreach (var (format, contentType, magic) in new[] { ("Pdf", "application/pdf", "%PDF"), ("Docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "PK"), ("Xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "PK") })
            {
                var export = await owner.PostJsonAsync($"/api/history/{historyId}/versions/2/export/{format}", new { });
                Assert.True(export.StatusCode == HttpStatusCode.OK, await export.Content.ReadAsStringAsync());
                Assert.Equal(contentType, export.Content.Headers.ContentType!.MediaType);
                var bytes = await export.Content.ReadAsByteArrayAsync();
                Assert.Equal(magic, System.Text.Encoding.ASCII.GetString(bytes, 0, magic.Length));
                Assert.EndsWith("-v2-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture), Path.GetFileNameWithoutExtension(export.Content.Headers.ContentDisposition!.FileName!.Trim('"'))[..^5], StringComparison.Ordinal);
            }

            Assert.Equal(HttpStatusCode.NotFound, (await other.PostJsonAsync($"/api/history/{historyId}/versions/2/export/Pdf", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await owner.PostJsonAsync($"/api/history/{historyId}/versions/9/export/Pdf", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostJsonAsync($"/api/history/{historyId}/versions/3/export/Pdf", new { })).StatusCode);
            var library = await (await owner.GetAsync("/api/history/reports")).ReadJsonAsync();
            Assert.Equal(3, library.GetProperty("totalCount").GetInt32());
            var docx = library.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("format").GetString() == "Docx");
            Assert.Equal("ExportDocx", docx.GetProperty("kind").GetString());
            Assert.Equal(2, docx.GetProperty("versionNumber").GetInt32());
            Assert.Equal(1, (await (await owner.GetAsync("/api/history/reports?format=Xlsx")).ReadJsonAsync()).GetProperty("totalCount").GetInt32());
            Assert.Empty((await (await other.GetAsync("/api/history/reports")).ReadJsonAsync()).GetProperty("items").EnumerateArray());
            var artifactId = docx.GetProperty("artifactId").GetGuid();
            var download = await owner.GetAsync($"/api/history/artifacts/{artifactId}");
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal("PK", System.Text.Encoding.ASCII.GetString((await download.Content.ReadAsByteArrayAsync()).Take(2).ToArray()));
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/history/artifacts/{artifactId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/history/artifacts/{artifactId}")).StatusCode);
            await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
            {
                var events = await context.ReportGenerationEvents.IgnoreQueryFilters().Where(e => e.HistoryId == historyId).ToListAsync();
                Assert.Equal(3, events.Count);
                Assert.All(events, e => Assert.Equal(Core.Entities.Analysis.ReportGenerationStatus.Completed, e.Status));
                Assert.All(events, e => Assert.True(e.SizeBytes > 0 && e.ArtifactId != null));
                events[0].SizeBytes = 1;
                await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            }

            // Rename, favorite, archive, collections, delete.
            Assert.Equal("Weekly ticket check", (await (await owner.PutAsJsonAsync(new Uri($"/api/history/{historyId}", UriKind.Relative), new { title = "Weekly ticket check" })).ReadJsonAsync()).GetProperty("title").GetString());
            Assert.True((await (await owner.PostJsonAsync($"/api/history/{historyId}/favorite", new { isFavorite = true })).ReadJsonAsync()).GetProperty("isFavorite").GetBoolean());
            Assert.Single((await (await owner.GetAsync("/api/history?favorite=true")).ReadJsonAsync()).GetProperty("items").EnumerateArray());
            var collection = await (await owner.PostJsonAsync("/api/history/collections", new { name = "Support KPIs", description = "weekly" })).ReadJsonAsync();
            var collectionId = collection.GetProperty("collectionId").GetGuid();
            Assert.Equal(HttpStatusCode.Conflict, (await owner.PostJsonAsync("/api/history/collections", new { name = "Support KPIs" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PostJsonAsync($"/api/history/collections/{collectionId}/items", new { historyId })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.PostJsonAsync($"/api/history/collections/{collectionId}/items", new { historyId })).StatusCode);
            Assert.Single((await (await owner.GetAsync($"/api/history?collectionId={collectionId}")).ReadJsonAsync()).GetProperty("items").EnumerateArray());
            Assert.Equal(1, (await (await owner.GetAsync("/api/history/collections")).ReadJsonAsync()).EnumerateArray().Single().GetProperty("itemCount").GetInt32());
            Assert.True((await (await owner.PostJsonAsync($"/api/history/{historyId}/archive", new { isArchived = true })).ReadJsonAsync()).GetProperty("isArchived").GetBoolean());
            Assert.Empty((await (await owner.GetAsync("/api/history")).ReadJsonAsync()).GetProperty("items").EnumerateArray());
            Assert.Single((await (await owner.GetAsync("/api/history?archived=true")).ReadJsonAsync()).GetProperty("items").EnumerateArray());
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(new Uri($"/api/history/{historyId}", UriKind.Relative))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/history/{historyId}")).StatusCode);
            Assert.Empty((await (await owner.GetAsync("/api/history?archived=true")).ReadJsonAsync()).GetProperty("items").EnumerateArray());
            await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
            {
                Assert.True(await context.AnalysisHistories.IgnoreQueryFilters().AnyAsync(h => h.HistoryId == historyId && h.IsDeleted));
                Assert.True(await context.AuditLogs.AnyAsync(a => a.EventType == "History.Deleted" && a.EntityId == historyId.ToString()));
            }
        }
        finally
        {
            FakeAiProvider.Script = null;
        }
    }

    private async Task<(Guid UserId, string Email)> InsertUserAsync(Guid organizationId, Guid roleId)
    {
        var email = $"history.{Guid.NewGuid():N}@org.test";
        var userId = Core.Common.Ids.New();
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var now = DateTimeOffset.UtcNow;
        context.OrganizationUsers.Add(TestData.NewUser(userId, organizationId, email, "History User"));
        context.LoginIdentities.Add(TestData.Identity(email, Core.Tenancy.PrincipalType.OrganizationUser, userId, organizationId, now));
        context.UserRoles.Add(new UserRole { OrganizationId = organizationId, UserId = userId, RoleId = roleId, AssignedAt = now, AssignedByAdministratorId = _db.Data.AdministratorId });
        await context.SaveChangesAsync();
        return (userId, email);
    }
}
