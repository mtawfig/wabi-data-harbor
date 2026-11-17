using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Ai;

/// <summary>
/// The clarifying question (P13.54) end to end: an open request on the Ask page gets one question back with suggestions
/// limited to what the asker may read; the answer (or a skip) goes on once and never asks again; re-runs never ask; an
/// organization can switch it off without touching another organization.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class ClarifyingQuestionTests : IDisposable
{
    private const string Allowed = "<<CLARIFICATION>>\nallowed\n<</CLARIFICATION>>";
    private const string NotAllowed = "<<CLARIFICATION>>\nnot allowed\n<</CLARIFICATION>>";
    private const string Open = "write email to manager about tickets";

    private readonly SqlServerTestDatabase _db;
    private readonly AiApiFactory _factory;
    private readonly string _keyRing = Path.Combine(Path.GetTempPath(), "wi-keys-tests", "ai-clarify-tests");

    public ClarifyingQuestionTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new AiApiFactory(db.ConnectionString, new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = _keyRing, ["Ai:Anthropic:ApiKey"] = "sk-ant-seeded-platform-key-0004" });
    }

    public void Dispose() => _factory.Dispose();

    private const string Plan = """
        {"objective":"Tickets by status","queries":[{"title":"","objective":"Tickets by status","source":{"schema":"dbo","object":"SupportTickets","alias":"t"},"joins":[],
         "select":[{"alias":"status","column":"t.Status","aggregate":null,"dateTrunc":null},{"alias":"tickets","column":null,"aggregate":"count","dateTrunc":null}],
         "filters":null,"groupBy":["status"],"having":[],"orderBy":[{"alias":"tickets","direction":"desc"}],"limit":null,"visualization":{"type":"bar","x":"status","y":["tickets"]}}],
         "assumptions":[],"deliverables":[]}
        """;

    // A question is a plan without queries: the question as objective, each suggestion as "label | objects". The second
    // suggestion is answered from an object the asker's snapshot does not hold: it must never reach the browser.
    private const string Asking = """
        {"objective":"What should the email cover about the tickets?","queries":[],"deliverables":["email"],
         "assumptions":["Tickets by status | dbo.SupportTickets","Payroll cost | dbo.Payslips","Tickets by impact | dbo.SupportTickets"]}
        """;

    [Fact]
    public async Task OpenRequest_GetsOneQuestionWithPermittedSuggestions_ThenGoesOnOnce()
    {
        // The fake model asks when it is allowed to and the request is the open one; a request containing "disobey" asks even when it is not allowed.
        FakeAiProvider.Script = request => request.SchemaName == "wabi_interpretation" || request.SchemaName != Core.Ai.AnalysisPlanSchema.Name
            ? """{"narrative":"ok","highlights":[],"subject":"Tickets","body":"Dear manager"}"""
            : request.UserContent.Contains("disobey", StringComparison.Ordinal) || (request.UserContent.Contains(Allowed, StringComparison.Ordinal) && request.UserContent.Contains(Open, StringComparison.Ordinal))
                ? Asking
                : Plan;
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
        var (applicationId, _) = await PrepareApplicationAsync(alice, "aiclarify");
        var roles = (await (await alice.GetAsync("/api/roles")).ReadJsonAsync()).EnumerateArray().ToList();
        var managementId = roles.Single(r => r.GetProperty("roleCode").GetString() == "Management").GetProperty("roleId").GetGuid();
        var (askerId, askerEmail) = await InsertUserAsync(_db.Data.OrganizationAId, managementId);
        using var asker = await LoginAsync(askerEmail);
        using var bob = await LoginAsync(TestData.BobEmail);

        // 1. The open request: a question comes back, nothing is run or saved.
        var historyBefore = await HistoryCountAsync(askerId);
        var first = await asker.PostJsonAsync("/api/ai/ask", new { applicationId, question = Open, language = "en" });
        Assert.True(first.StatusCode == HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var asked = await first.ReadJsonAsync();
        Assert.Equal("What should the email cover about the tickets?", asked.GetProperty("clarification").GetProperty("question").GetString());
        Assert.Equal(["Tickets by status", "Tickets by impact"], asked.GetProperty("clarification").GetProperty("options").EnumerateArray().Select(o => o.GetString()!).ToArray());
        var body = await first.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Payroll", body, StringComparison.Ordinal);
        Assert.DoesNotContain("dbo.", body, StringComparison.Ordinal);
        Assert.Equal(0, asked.GetProperty("panels").GetArrayLength());
        Assert.False(asked.TryGetProperty("historyId", out _));
        Assert.Contains(Allowed, PlannerRequest(Open), StringComparison.Ordinal);
        Assert.Equal(historyBefore, await HistoryCountAsync(askerId));
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            // Recorded: the question asked (with how many suggestions survived) and the model call in the AI usage ledger.
            var events = await context.UsageEvents.IgnoreQueryFilters().Where(e => e.ActorId == askerId && e.EventType == "Ai.ClarificationAsked").ToListAsync();
            var asking = Assert.Single(events);
            Assert.Contains("\"options\":2", asking.MetadataJson, StringComparison.Ordinal);
            Assert.Contains("\"suggested\":3", asking.MetadataJson, StringComparison.Ordinal);
            Assert.Equal(1, await context.AiCalls.IgnoreQueryFilters().CountAsync(c => c.UserId == askerId && c.Purpose == "Plan"));
        }

        // 2. The asker picks a suggestion: the request goes on with it, is not asked again, and is saved with the choice.
        var second = await asker.PostJsonAsync("/api/ai/ask", new { applicationId, question = Open, language = "en", clarification = "Tickets by status" });
        Assert.True(second.StatusCode == HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        var answered = await second.ReadJsonAsync();
        Assert.False(answered.TryGetProperty("clarification", out _));
        Assert.True(answered.GetProperty("panels").GetArrayLength() > 0);
        var composed = Open + " — Tickets by status";
        var followUp = PlannerRequest(composed);
        Assert.Contains(NotAllowed, followUp, StringComparison.Ordinal);
        Assert.DoesNotContain(Allowed, followUp, StringComparison.Ordinal);
        var historyId = answered.GetProperty("historyId").GetGuid();
        Assert.Equal(composed, (await (await asker.GetAsync($"/api/history/{historyId}")).ReadJsonAsync()).GetProperty("originalPrompt").GetString());

        // 3. "Answer anyway": answered as understood, never asked.
        var skipped = await asker.PostJsonAsync("/api/ai/ask", new { applicationId, question = Open, language = "en", skipClarification = true });
        Assert.True(skipped.StatusCode == HttpStatusCode.OK, await skipped.Content.ReadAsStringAsync());
        Assert.False((await skipped.ReadJsonAsync()).TryGetProperty("clarification", out _));
        Assert.Contains(NotAllowed, FakeAiProvider.Requests.Last(r => r.SchemaName == Core.Ai.AnalysisPlanSchema.Name).UserContent, StringComparison.Ordinal);
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            var events = await context.UsageEvents.IgnoreQueryFilters().Where(e => e.ActorId == askerId && e.EventType == "Ai.ClarificationAnswered").Select(e => e.MetadataJson).ToListAsync();
            Assert.Equal(2, events.Count);
            Assert.Single(events, e => e!.Contains("\"skipped\":true", StringComparison.Ordinal));
            Assert.Single(events, e => e!.Contains("\"skipped\":false", StringComparison.Ordinal));
        }

        // 4. A model that asks where no question is allowed is refused: the asker never gets a second question.
        var disobeyed = await asker.PostJsonAsync("/api/ai/ask", new { applicationId, question = "disobey and ask again", language = "en", clarification = "anything" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, disobeyed.StatusCode);
        Assert.Contains("plan_clarification_not_allowed", await disobeyed.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // 5. A re-run of a saved answer never asks.
        var before = FakeAiProvider.Requests.Count;
        Assert.Equal(HttpStatusCode.OK, (await asker.PostJsonAsync($"/api/history/{historyId}/rerun", new { })).StatusCode);
        Assert.Contains(NotAllowed, FakeAiProvider.Requests.Skip(before).First(r => r.SchemaName == Core.Ai.AnalysisPlanSchema.Name).UserContent, StringComparison.Ordinal);

        // 6. The organization switches clarifying questions off (administrators only); the other organization keeps them.
        var profile = await (await alice.GetAsync("/api/organization")).ReadJsonAsync();
        Assert.True(profile.GetProperty("askClarifyingQuestions").GetBoolean());
        object Settings(bool ask) => new
        {
            organizationName = profile.GetProperty("organizationName").GetString(), organizationNameAr = profile.GetProperty("organizationNameAr").GetString(),
            timeZone = profile.GetProperty("timeZone").GetString(), defaultLanguage = profile.GetProperty("defaultLanguage").GetString(), askClarifyingQuestions = ask,
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await asker.PutAsJsonAsync(new Uri("/api/organization", UriKind.Relative), Settings(false))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.PutAsJsonAsync(new Uri("/api/organization", UriKind.Relative), Settings(false))).StatusCode);
        try
        {
            Assert.False((await (await alice.GetAsync("/api/organization")).ReadJsonAsync()).GetProperty("askClarifyingQuestions").GetBoolean());
            Assert.True((await (await bob.GetAsync("/api/organization")).ReadJsonAsync()).GetProperty("askClarifyingQuestions").GetBoolean());
            var off = await asker.PostJsonAsync("/api/ai/ask", new { applicationId, question = Open, language = "en" });
            Assert.True(off.StatusCode == HttpStatusCode.OK, await off.Content.ReadAsStringAsync());
            Assert.False((await off.ReadJsonAsync()).TryGetProperty("clarification", out _));
            Assert.Contains(NotAllowed, FakeAiProvider.Requests.Last(r => r.SchemaName == Core.Ai.AnalysisPlanSchema.Name).UserContent, StringComparison.Ordinal);
        }
        finally
        {
            // A profile saved without the setting keeps it: older clients cannot switch it by accident.
            Assert.Equal(HttpStatusCode.OK, (await alice.PutAsJsonAsync(new Uri("/api/organization", UriKind.Relative), Settings(true))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await alice.PutAsJsonAsync(new Uri("/api/organization", UriKind.Relative), new { organizationName = profile.GetProperty("organizationName").GetString(), organizationNameAr = profile.GetProperty("organizationNameAr").GetString(), timeZone = profile.GetProperty("timeZone").GetString(), defaultLanguage = profile.GetProperty("defaultLanguage").GetString() })).StatusCode);
        Assert.True((await (await alice.GetAsync("/api/organization")).ReadJsonAsync()).GetProperty("askClarifyingQuestions").GetBoolean());
    }

    /// <summary>The latest planner request that carried this question.</summary>
    private static string PlannerRequest(string question)
        => FakeAiProvider.Requests.Last(r => r.SchemaName == Core.Ai.AnalysisPlanSchema.Name && r.UserContent.Contains("\n" + question + "\n<</QUESTION>>", StringComparison.Ordinal)).UserContent;

    private async Task<int> HistoryCountAsync(Guid userId)
    {
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        return await context.AnalysisHistories.IgnoreQueryFilters().CountAsync(h => h.OwnerUserId == userId);
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
        var email = $"clarify.{Guid.NewGuid():N}@org.test";
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
