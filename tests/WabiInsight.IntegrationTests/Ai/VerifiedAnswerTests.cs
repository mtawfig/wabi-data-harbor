using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Entities.Analysis;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Ai;

/// <summary>
/// Verified answers (P13.53) end to end with two organizations: the asker's verdict, the Organization Administrator's review,
/// the approved answer given to the planner as an example for a similar question (values removed where the column could
/// point at a person or record), only for askers who may run it, set aside when its columns change, and audited.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class VerifiedAnswerTests : IDisposable
{
    private const string ExamplesBlock = "<<VERIFIED EXAMPLES>>";

    private readonly SqlServerTestDatabase _db;
    private readonly AiApiFactory _factory;
    private readonly string _keyRing = Path.Combine(Path.GetTempPath(), "wi-keys-tests", "ai-verified-tests");

    public VerifiedAnswerTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new AiApiFactory(db.ConnectionString, new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = _keyRing, ["Ai:Anthropic:ApiKey"] = "sk-ant-seeded-platform-key-0003" });
    }

    public void Dispose() => _factory.Dispose();

    private static string Plan(string impact) => $$"""
        {"objective":"Tickets by status","queries":[{"title":"","objective":"Tickets by status","source":{"schema":"dbo","object":"SupportTickets","alias":"t"},"joins":[],
         "select":[{"alias":"status","column":"t.Status","aggregate":null,"dateTrunc":null},{"alias":"tickets","column":null,"aggregate":"count","dateTrunc":null}],
         "filters":{"logic":"and","conditions":[{"column":"t.Impact","operator":"neq","values":["{{impact}}"]}],"groups":[]},
         "groupBy":["status"],"orderBy":[{"alias":"tickets","direction":"desc"}],"limit":null,"visualization":{"type":"bar","x":"status","y":["tickets"]},"assumptions":[]}],"assumptions":[],"deliverables":[]}
        """;

    [Fact]
    public async Task VerifiedAnswers_FromVerdictToReviewedExample_StayInsideTheOrganizationAndTheAskersPermissions()
    {
        FakeAiProvider.Script = request => request.SchemaName == "wabi_interpretation" ? """{"narrative":"ok","highlights":[]}""" : Plan("Critical");
        try
        {
            await RunAsync();
        }
        finally
        {
            FakeAiProvider.Script = null;
        }
    }

    private async Task RunAsync()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        var (applicationId, columns) = await PrepareApplicationAsync(alice, "aiverif");
        var roles = (await (await alice.GetAsync("/api/roles")).ReadJsonAsync()).EnumerateArray().ToList();
        var managementId = roles.Single(r => r.GetProperty("roleCode").GetString() == "Management").GetProperty("roleId").GetGuid();
        var (_, askerEmail) = await InsertUserAsync(_db.Data.OrganizationAId, managementId);
        var (_, colleagueEmail) = await InsertUserAsync(_db.Data.OrganizationAId, managementId);
        using var asker = await LoginAsync(askerEmail);
        using var colleague = await LoginAsync(colleagueEmail);
        using var bob = await LoginAsync(TestData.BobEmail);
        using var platform = await LoginAsync(TestData.AdminEmail, TestData.AdminPassword);

        // An analyst who may read the tickets' status but not their impact.
        var analystId = roles.Single(r => r.GetProperty("roleCode").GetString() == "Analyst").GetProperty("roleId").GetGuid();
        var ticketsObject = (await (await alice.GetAsync($"/api/metadata/applications/{applicationId}/objects?search=SupportTickets&pageSize=10")).ReadJsonAsync()).GetProperty("items").EnumerateArray().Single(o => o.GetProperty("schemaName").GetString() == "dbo" && o.GetProperty("objectName").GetString() == "SupportTickets").GetProperty("objectId").GetGuid();
        var statusColumn = columns.Single(c => c.GetProperty("columnName").GetString() == "Status").GetProperty("columnId").GetGuid();
        var impactColumn = columns.Single(c => c.GetProperty("columnName").GetString() == "Impact").GetProperty("columnId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await alice.PutAsJsonAsync(new Uri($"/api/permissions/roles/{analystId}/objects/{ticketsObject}", UriKind.Relative), new { granted = true, allColumns = false, columnIds = new[] { statusColumn }, rowScope = "All" })).StatusCode);
        var (_, analystEmail) = await InsertUserAsync(_db.Data.OrganizationAId, analystId);
        using var analyst = await LoginAsync(analystEmail);

        // The asker gets an answer and says it is correct; earlier verdicts are kept, the latest counts.
        var ask = await asker.PostJsonAsync("/api/ai/ask", new { applicationId, question = "How many tickets per status, excluding critical ones?", language = "en" });
        Assert.True(ask.StatusCode == HttpStatusCode.OK, await ask.Content.ReadAsStringAsync());
        var answer = await ask.ReadJsonAsync();
        var historyId = answer.GetProperty("historyId").GetGuid();
        var feedbackPath = $"/api/history/{historyId}/versions/{answer.GetProperty("versionNumber").GetInt32()}/feedback";
        Assert.False((await (await asker.GetAsync(feedbackPath)).ReadJsonAsync()).TryGetProperty("verdict", out _));
        Assert.Equal(HttpStatusCode.BadRequest, (await asker.PostJsonAsync(feedbackPath, new { verdict = "Maybe" })).StatusCode);

        // Nobody else gives a verdict on it: not a colleague, not another organization, not a Platform Administrator.
        Assert.Equal(HttpStatusCode.NotFound, (await colleague.PostJsonAsync(feedbackPath, new { verdict = "Correct" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostJsonAsync(feedbackPath, new { verdict = "Correct" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.PostJsonAsync(feedbackPath, new { verdict = "Correct" })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await asker.PostJsonAsync(feedbackPath, new { verdict = "NotCorrect", reason = "Counts look low" })).StatusCode);
        var given = await (await asker.PostJsonAsync(feedbackPath, new { verdict = "correct" })).ReadJsonAsync();
        Assert.Equal("Correct", given.GetProperty("verdict").GetString());
        Assert.Equal("Correct", (await (await asker.GetAsync(feedbackPath)).ReadJsonAsync()).GetProperty("verdict").GetString());

        // Review is for Organization Administrators of the same organization only.
        Assert.Equal(HttpStatusCode.Forbidden, (await asker.GetAsync("/api/verified-answers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync("/api/verified-answers")).StatusCode);
        var waiting = await (await alice.GetAsync($"/api/verified-answers?status=Pending&applicationId={applicationId}")).ReadJsonAsync();
        var item = waiting.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("question").GetString() == "How many tickets per status, excluding critical ones?");
        var id = item.GetProperty("verifiedAnswerId").GetGuid();
        Assert.Equal("Ask User", item.GetProperty("askedByName").GetString());
        Assert.True((await (await alice.GetAsync("/api/verified-answers/counts")).ReadJsonAsync()).GetProperty("pending").GetInt32() >= 1);
        var bobList = await bob.GetAsync("/api/verified-answers");
        if (bobList.StatusCode == HttpStatusCode.OK)
        {
            Assert.DoesNotContain((await bobList.ReadJsonAsync()).GetProperty("items").EnumerateArray(), i => i.GetProperty("verifiedAnswerId").GetGuid() == id);
        }

        Assert.Contains((await bob.GetAsync($"/api/verified-answers/{id}")).StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden });
        Assert.Contains((await bob.PostJsonAsync($"/api/verified-answers/{id}/review", new { action = "approve" })).StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden });

        // The administrator sees the question, the asker's verdicts and how it was computed; Alice may read no data, so not the result.
        var detail = await (await alice.GetAsync($"/api/verified-answers/{id}")).ReadJsonAsync();
        Assert.Equal("Pending", detail.GetProperty("status").GetString());
        Assert.Equal("dbo", detail.GetProperty("plan").GetProperty("queries")[0].GetProperty("source").GetProperty("schema").GetString());
        Assert.True(detail.GetProperty("resultHidden").GetBoolean());
        Assert.False(detail.TryGetProperty("result", out _));
        Assert.Equal(JsonValueKind.Array, detail.GetProperty("panelLabels").ValueKind);
        Assert.Equal(2, detail.GetProperty("feedback").GetArrayLength());

        // Not approved yet: a similar question gets no example.
        Assert.DoesNotContain(ExamplesBlock, await PlannerRequestAsync(colleague, applicationId, "Tickets per status excluding critical"), StringComparison.Ordinal);

        // Approved: the colleague's similar question carries it as an example; the impact is an internal code, so its value stays.
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostJsonAsync($"/api/verified-answers/{id}/review", new { action = "promote" })).StatusCode);
        var approved = await (await alice.PostJsonAsync($"/api/verified-answers/{id}/review", new { action = "approve", note = "Checked against the ticket list" })).ReadJsonAsync();
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        var request = await PlannerRequestAsync(colleague, applicationId, "Tickets per status excluding critical");
        var examples = Block(request);
        Assert.Contains("How many tickets per status, excluding critical ones?", examples, StringComparison.Ordinal);
        Assert.Contains("\"values\":[\"Critical\"]", examples, StringComparison.Ordinal);
        Assert.True(request.IndexOf(ExamplesBlock, StringComparison.Ordinal) < request.IndexOf("<<LANGUAGE>>", StringComparison.Ordinal));
        // An unrelated question gets none.
        Assert.DoesNotContain(ExamplesBlock, await PlannerRequestAsync(colleague, applicationId, "Average resolution hours by month"), StringComparison.Ordinal);

        // Once the impact column is confidential, its value is removed from the example and from the example's question.
        await UpdateColumnAsync(impactColumn, c => c.Classification = DataClassification.Confidential);
        examples = Block(await PlannerRequestAsync(colleague, applicationId, "Tickets per status excluding critical"));
        Assert.Contains("\"values\":[\"[value]\"]", examples, StringComparison.Ordinal);
        Assert.DoesNotContain("Critical", examples, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("excluding [value] ones", examples, StringComparison.Ordinal);
        await UpdateColumnAsync(impactColumn, c => c.Classification = DataClassification.Internal);

        // The analyst may not read the impact: the example is never shown to the model for them.
        Assert.DoesNotContain(ExamplesBlock, await PlannerRequestAsync(analyst, applicationId, "Tickets per status excluding critical"), StringComparison.Ordinal);

        // A later "not correct" does not change how the platform answers: it stays approved and is flagged for the administrators.
        Assert.Equal(HttpStatusCode.OK, (await asker.PostJsonAsync(feedbackPath, new { verdict = "NotCorrect", reason = "Closed tickets are missing" })).StatusCode);
        detail = await (await alice.GetAsync($"/api/verified-answers/{id}")).ReadJsonAsync();
        Assert.Equal("Approved", detail.GetProperty("status").GetString());
        Assert.Equal("NotCorrect", detail.GetProperty("latestVerdict").GetString());
        Assert.True(detail.GetProperty("useCount").GetInt32() >= 2);
        Assert.Contains((await (await alice.GetAsync("/api/verified-answers?status=Waiting")).ReadJsonAsync()).GetProperty("items").EnumerateArray(), i => i.GetProperty("verifiedAnswerId").GetGuid() == id);

        // Feedback is append-only.
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            var rows = await context.AnswerFeedbacks.IgnoreQueryFilters().Where(f => f.HistoryId == historyId).ToListAsync();
            Assert.Equal(3, rows.Count);
            rows[0].Reason = "changed";
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        // A column it uses disappears from the catalogue: the answer is set aside and cannot be enabled until it is back.
        await UpdateColumnAsync(impactColumn, c => c.IsPresent = false);
        var setAside = (await (await alice.GetAsync($"/api/verified-answers?applicationId={applicationId}")).ReadJsonAsync()).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("verifiedAnswerId").GetGuid() == id);
        Assert.Equal("SetAside", setAside.GetProperty("status").GetString());
        Assert.Contains("Impact", (await (await alice.GetAsync($"/api/verified-answers/{id}")).ReadJsonAsync()).GetProperty("setAsideReason").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PostJsonAsync($"/api/verified-answers/{id}/review", new { action = "enable" })).StatusCode);
        await UpdateColumnAsync(impactColumn, c => c.IsPresent = true);
        Assert.Equal("Approved", (await (await alice.PostJsonAsync($"/api/verified-answers/{id}/review", new { action = "enable" })).ReadJsonAsync()).GetProperty("status").GetString());

        // Disabled: no longer used.
        Assert.Equal("Disabled", (await (await alice.PostJsonAsync($"/api/verified-answers/{id}/review", new { action = "disable" })).ReadJsonAsync()).GetProperty("status").GetString());
        Assert.DoesNotContain(ExamplesBlock, await PlannerRequestAsync(colleague, applicationId, "Tickets per status excluding critical"), StringComparison.Ordinal);

        // At most 200 approved answers per application.
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            var source = await context.VerifiedAnswers.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.VerifiedAnswerId == id);
            for (var i = 0; i < Core.Ai.VerifiedExamples.MaxApprovedPerApplication; i++)
            {
                context.VerifiedAnswers.Add(new VerifiedAnswer
                {
                    VerifiedAnswerId = Core.Common.Ids.New(), OrganizationId = source.OrganizationId, ApplicationId = applicationId, HistoryId = historyId, VersionNumber = 1000 + i,
                    AskedByUserId = source.AskedByUserId, Question = $"Filler {i}", Language = "en", PlanJson = source.PlanJson, Terms = "filler", Status = VerifiedAnswerStatuses.Approved,
                    LatestVerdict = AnswerVerdicts.Correct, LatestFeedbackAt = DateTimeOffset.UtcNow, ReviewedAt = DateTimeOffset.UtcNow,
                });
            }

            await context.SaveChangesAsync();
        }

        var limit = await alice.PostJsonAsync($"/api/verified-answers/{id}/review", new { action = "enable" });
        Assert.Equal(HttpStatusCode.Conflict, limit.StatusCode);
        Assert.Contains("verified_answer_limit_reached", await limit.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Deleted; every review step is in the audit log.
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync(new Uri($"/api/verified-answers/{id}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/verified-answers/{id}")).StatusCode);
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            var events = await context.AuditLogs.IgnoreQueryFilters().Where(a => a.EntityId == id.ToString()).Select(a => a.EventType).ToListAsync();
            Assert.Equal(3, events.Count(e => e == "VerifiedAnswer.Reviewed"));
            Assert.Contains("VerifiedAnswer.SetAside", events);
            Assert.Contains("VerifiedAnswer.Deleted", events);
            Assert.True(await context.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.EventType == "Answer.FeedbackGiven" && a.EntityId == historyId.ToString()));
        }
    }

    /// <summary>Asks a question and returns the planner request the model received (the answer itself may fail authorization).</summary>
    private static async Task<string> PlannerRequestAsync(HttpClient client, Guid applicationId, string question)
    {
        var before = FakeAiProvider.Requests.Count;
        await client.PostJsonAsync("/api/ai/ask", new { applicationId, question, language = "en" });
        return FakeAiProvider.Requests.Skip(before).Last(r => r.SchemaName != "wabi_interpretation" && r.UserContent.Contains(question, StringComparison.Ordinal)).UserContent;
    }

    private static string Block(string request)
    {
        var start = request.IndexOf(ExamplesBlock, StringComparison.Ordinal);
        Assert.True(start >= 0, "The planner request carries no verified examples.");
        var end = request.IndexOf("<</VERIFIED EXAMPLES>>", start, StringComparison.Ordinal);
        return request[start..end];
    }

    private async Task UpdateColumnAsync(Guid columnId, Action<DatabaseColumn> change)
    {
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var column = await context.DatabaseColumns.IgnoreQueryFilters().SingleAsync(c => c.ColumnId == columnId);
        change(column);
        await context.SaveChangesAsync();
    }

    private async Task<HttpClient> LoginAsync(string email, string password = TestData.UserPassword)
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, password)).StatusCode);
        return client;
    }

    private async Task<(Guid ApplicationId, List<JsonElement> Columns)> PrepareApplicationAsync(HttpClient alice, string tag)
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
        return (applicationId, approved.GetProperty("columns").EnumerateArray().ToList());
    }

    private async Task<(Guid UserId, string Email)> InsertUserAsync(Guid organizationId, Guid? roleId)
    {
        var email = $"verify.{Guid.NewGuid():N}@org.test";
        var userId = Core.Common.Ids.New();
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var now = DateTimeOffset.UtcNow;
        context.OrganizationUsers.Add(TestData.NewUser(userId, organizationId, email, "Ask User"));
        context.LoginIdentities.Add(TestData.Identity(email, Core.Tenancy.PrincipalType.OrganizationUser, userId, organizationId, now));
        if (roleId is { } r)
        {
            context.UserRoles.Add(new UserRole { OrganizationId = organizationId, UserId = userId, RoleId = r, AssignedAt = now, AssignedByAdministratorId = _db.Data.AdministratorId });
        }

        await context.SaveChangesAsync();
        return (userId, email);
    }
}
