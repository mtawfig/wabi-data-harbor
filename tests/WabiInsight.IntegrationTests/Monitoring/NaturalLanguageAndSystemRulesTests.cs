using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Ai;
using WabiInsight.Core.Entities.Email;
using WabiInsight.Core.Entities.Monitoring;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Monitoring;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Monitoring;
using WabiInsight.IntegrationTests.Ai;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Monitoring;

/// <summary>
/// P14.4 (docs/PROACTIVE_AGENT.md §14): a rule described in Arabic binds to the right columns and needs
/// confirmation; an invalid draft is rejected with a clear message; removing a column pauses dependent rules with an
/// alert. System rules read the organization's own platform signals, are administrator-only and never cross organizations.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class NaturalLanguageAndSystemRulesTests : IDisposable
{
    private const string ArabicDescription = "نبهني عندما يُستخدم نفس الحساب من أكثر من جهاز في نفس اليوم";

    private readonly SqlServerTestDatabase _db;
    private readonly AiApiFactory _factory;
    private readonly string _keyRing = Path.Combine(Path.GetTempPath(), "wi-keys-tests", "nl-system-rules-tests");

    public NaturalLanguageAndSystemRulesTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new AiApiFactory(db.ConnectionString, new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = _keyRing, ["Ai:Anthropic:ApiKey"] = "sk-ant-seeded-platform-key-0004" });
    }

    public void Dispose()
    {
        FakeAiProvider.Script = null;
        _factory.Dispose();
    }

    [Fact]
    public async Task ArabicDescription_BindsToTheOwnersColumns_IsSavedOnlyOnConfirmation_AndInvalidDraftsAreRejected()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        await CreateSignInTableAsync("DraftPortalSignIns");
        var (_, objectId, columns) = await PrepareApplicationAsync(alice, "drafts", "DraftPortalSignIns");
        Guid Column(string name) => columns.Single(c => c.GetProperty("columnName").GetString() == name).GetProperty("columnId").GetGuid();
        await SetRolesAsync(alice, Column);
        var (managerId, managerEmail) = await InsertUserAsync(_db.Data.OrganizationAId, await RoleIdAsync(alice, "Management"), "Draft Manager");
        var (_, analystEmail) = await InsertUserAsync(_db.Data.OrganizationAId, await RoleIdAsync(alice, "Analyst"), "Draft Analyst");
        using var manager = await LoginAsync(managerEmail);
        using var analyst = await LoginAsync(analystEmail);

        FakeAiProvider.Requests.Clear();
        FakeAiProvider.Script = request => request.SchemaName == RuleDraftSchema.Name ? DraftJson() : "{}";
        var before = (await (await manager.GetAsync("/api/monitoring/rules")).ReadJsonAsync()).GetArrayLength();

        // 1. The Arabic description becomes a draft bound to catalogue ids of the owner's permitted columns.
        var response = await manager.PostJsonAsync("/api/monitoring/rules/draft", new { description = ArabicDescription, language = "ar" });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var draft = await response.ReadJsonAsync();
        // Drafting a rule reaches a model like a question does: it is on the AI usage ledger and counts as spending (P13.48).
        await using (var ledger = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            var drafted = await ledger.AiCalls.IgnoreQueryFilters().Where(c => c.Purpose == AiCallPurposes.RuleDraft && c.OrganizationId == _db.Data.OrganizationAId).OrderByDescending(c => c.CreatedAt).FirstAsync();
            Assert.True(drafted.Succeeded);
            Assert.Null(drafted.RunId);
            Assert.True(drafted.InputTokens > 0 && drafted.TotalCost > 0);
        }

        var rule = draft.GetProperty("rule");
        var data = rule.GetProperty("definition").GetProperty("data");
        Assert.Equal("Data", rule.GetProperty("kind").GetString());
        Assert.Equal(objectId, data.GetProperty("objectId").GetGuid());
        Assert.Equal(Column("LoginAt"), data.GetProperty("timeColumnId").GetGuid());
        Assert.Equal([Column("AccountNumber")], data.GetProperty("groupByColumnIds").EnumerateArray().Select(e => e.GetGuid()));
        Assert.Equal("count_distinct", data.GetProperty("measure").GetProperty("aggregate").GetString());
        Assert.Equal(Column("DeviceId"), data.GetProperty("measure").GetProperty("columnId").GetGuid());
        Assert.Equal("نفس الحساب من أكثر من جهاز", rule.GetProperty("name").GetString());
        Assert.Equal(ArabicDescription, rule.GetProperty("description").GetString());
        Assert.Equal("High", rule.GetProperty("severity").GetString());
        var bindings = draft.GetProperty("bindings").EnumerateArray().Select(b => (b.GetProperty("part").GetString(), b.GetProperty("columnName").GetString())).ToList();
        Assert.Equal([("time", "LoginAt"), ("group", "AccountNumber"), ("measure", "DeviceId")], bindings);
        Assert.Contains("الحسابات", draft.GetProperty("explanation").GetString(), StringComparison.Ordinal);
        Assert.Single(draft.GetProperty("assumptions").EnumerateArray());

        // The model saw the owner's catalogue and the description fenced as data, never ids or rows.
        var request = Assert.Single(FakeAiProvider.Requests, r => r.SchemaName == RuleDraftSchema.Name);
        Assert.Contains("<<DESCRIPTION>>", request.UserContent, StringComparison.Ordinal);
        Assert.Contains(ArabicDescription, request.UserContent, StringComparison.Ordinal);
        Assert.Contains("dbo.DraftPortalSignIns", request.UserContent, StringComparison.Ordinal);
        Assert.Contains("DeviceId (", request.UserContent, StringComparison.Ordinal);
        Assert.Contains("role DeviceIdentifier", request.UserContent, StringComparison.Ordinal);
        Assert.DoesNotContain(objectId.ToString(), request.UserContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DEV-A", request.UserContent, StringComparison.Ordinal);

        // 2. Nothing is saved until the user confirms.
        Assert.Equal(before, (await (await manager.GetAsync("/api/monitoring/rules")).ReadJsonAsync()).GetArrayLength());
        await using (var context = _db.CreateContext(TestData.SystemPrincipal))
        {
            Assert.True(await context.UsageEvents.IgnoreQueryFilters().AnyAsync(e => e.EventType == "Monitoring.RuleDrafted" && e.OrganizationId == _db.Data.OrganizationAId && e.ActorId == managerId));
        }

        var saved = await manager.PostAsJsonAsync("/api/monitoring/rules", rule);
        Assert.True(saved.StatusCode == HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        var ruleId = (await saved.ReadJsonAsync()).GetProperty("ruleId").GetGuid();
        var test = await (await manager.PostAsync($"/api/monitoring/rules/{ruleId}/test", null)).ReadJsonAsync();
        Assert.Equal("Fired", test.GetProperty("outcome").GetString());
        Assert.Equal("ACC-1", Assert.Single(test.GetProperty("matches").EnumerateArray()).GetProperty("groupKey").GetString());

        // 3. Invalid drafts are rejected with a clear code, and nothing is saved.
        async Task AssertRejected(HttpClient client, object body, string code)
        {
            var refused = await client.PostJsonAsync("/api/monitoring/rules/draft", body);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal(code, (await refused.ReadJsonAsync()).GetProperty("detail").GetString());
        }

        var arabic = new { description = ArabicDescription, language = "ar" };
        FakeAiProvider.Script = r => DraftJson().Replace("\"DeviceId\"", "\"NoSuchColumn\"", StringComparison.Ordinal);
        await AssertRejected(manager, arabic, "draft_reference_not_permitted");
        FakeAiProvider.Script = r => DraftJson().Replace("\"DraftPortalSignIns\"", "\"Payslips\"", StringComparison.Ordinal);
        await AssertRejected(manager, arabic, "draft_reference_not_permitted");
        FakeAiProvider.Script = r => DraftJson().Replace("\"expressible\":true", "\"expressible\":false", StringComparison.Ordinal).Replace("\"reason\":null", "\"reason\":\"needs two tables\"", StringComparison.Ordinal);
        await AssertRejected(manager, arabic, "draft_not_expressible");
        FakeAiProvider.Script = r => "not json at all";
        await AssertRejected(manager, arabic, "draft_unreadable");
        // A bound but invalid shape (count_distinct without a column) fails the same validation as a saved rule.
        FakeAiProvider.Script = r => DraftJson().Replace("\"column\":\"DeviceId\"", "\"column\":null", StringComparison.Ordinal);
        await AssertRejected(manager, arabic, "rule_measure_column_required");
        await AssertRejected(manager, new { description = "hi", language = "en" }, "draft_description_invalid");
        Assert.Equal(before + 1, (await (await manager.GetAsync("/api/monitoring/rules")).ReadJsonAsync()).GetArrayLength());

        // Analysts do not author rules; an administrator drafting for someone without access to the table is refused.
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.PostJsonAsync("/api/monitoring/rules/draft", arabic)).StatusCode);
        FakeAiProvider.Script = r => DraftJson();
        await AssertRejected(alice, new { description = ArabicDescription, language = "ar", ownerUserId = _db.Data.UserA1Id }, "draft_no_permitted_objects");
    }

    [Fact]
    public async Task RemovingAColumn_PausesDependentRulesImmediately_WithAnAlert_AndTheDriftRuleReportsIt()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        await CreateSignInTableAsync("DriftPortalSignIns");
        var (applicationId, objectId, columns) = await PrepareApplicationAsync(alice, "drift", "DriftPortalSignIns");
        Guid Column(string name) => columns.Single(c => c.GetProperty("columnName").GetString() == name).GetProperty("columnId").GetGuid();
        await SetRolesAsync(alice, Column);
        var (managerId, managerEmail) = await InsertUserAsync(_db.Data.OrganizationAId, await RoleIdAsync(alice, "Management"), "Drift Manager");
        using var manager = await LoginAsync(managerEmail);

        var dependent = await CreateAsync(manager, DataRuleBody("Devices per account (drift)", objectId, Column("AccountNumber"), Column("LoginAt"), Column("DeviceId")));
        var independent = await CreateAsync(manager, DataRuleBody("Sign-ins per account (drift)", objectId, Column("AccountNumber"), Column("LoginAt"), null));
        var drift = await CreateAsync(alice, SystemRuleBody("Catalogue drift", RuleTemplates.MetadataDrift, "metadata_drift", 0));

        // The source drops DeviceId; the administrator refreshes the catalogue.
        await ExecuteAsync("ALTER TABLE dbo.DriftPortalSignIns DROP COLUMN DeviceId;");
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/metadata/applications/{applicationId}/refresh", new { })).StatusCode);

        var paused = await (await manager.GetAsync($"/api/monitoring/rules/{dependent}")).ReadJsonAsync();
        Assert.False(paused.GetProperty("isActive").GetBoolean());
        Assert.Equal("rule_column_unavailable", paused.GetProperty("lastError").GetString());
        Assert.False(paused.TryGetProperty("nextRunAt", out _));
        Assert.True((await (await manager.GetAsync($"/api/monitoring/rules/{independent}")).ReadJsonAsync()).GetProperty("isActive").GetBoolean());
        var flagged = Assert.Single((await (await manager.GetAsync($"/api/monitoring/alerts?ruleId={dependent}")).ReadJsonAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal("rule_invalid", flagged.GetProperty("groupKey").GetString());
        Assert.Equal("rule_column_unavailable", flagged.GetProperty("summary").GetProperty("reason").GetString());
        await using (var context = _db.CreateContext(TestData.SystemPrincipal))
        {
            Assert.True(await context.Notifications.IgnoreQueryFilters().AnyAsync(n => n.NotificationType == "Monitoring.RulePaused" && n.RecipientId == managerId && n.EntityId == dependent.ToString()));
            Assert.False((await context.ScheduledJobs.SingleAsync(j => j.JobType == ScheduledJobType.MonitoringRule && j.TargetId == dependent)).IsEnabled);
        }

        // A second refresh does not raise a second alert: the rule is already paused.
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/metadata/applications/{applicationId}/refresh", new { })).StatusCode);
        Assert.Single((await (await manager.GetAsync($"/api/monitoring/alerts?ruleId={dependent}")).ReadJsonAsync()).GetProperty("items").EnumerateArray());
        // Resuming re-validates the definition against the catalogue.
        var resume = await manager.PostAsync($"/api/monitoring/rules/{dependent}/resume", null);
        Assert.Equal(HttpStatusCode.BadRequest, resume.StatusCode);
        Assert.Equal("rule_column_unavailable", (await resume.ReadJsonAsync()).GetProperty("detail").GetString());

        // Un-approving a column the rule reads pauses it too.
        var put = await alice.PutAsJsonAsync($"/api/metadata/columns/{Column("AccountNumber")}", new { displayNameEn = "Account", displayNameAr = (string?)null, businessDescription = (string?)null, classification = "Confidential", isApproved = false, semanticRole = "AccountIdentifier" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.False((await (await manager.GetAsync($"/api/monitoring/rules/{independent}")).ReadJsonAsync()).GetProperty("isActive").GetBoolean());

        // The system drift rule reports the removed column; it holds no business values.
        await RunRuleAsync(drift);
        var driftAlerts = (await (await alice.GetAsync($"/api/monitoring/alerts?ruleId={drift}")).ReadJsonAsync()).GetProperty("items").EnumerateArray().ToList();
        var removed = Assert.Single(driftAlerts, a => a.GetProperty("groupKey").GetString() == "DRIFT · dbo.DriftPortalSignIns");
        Assert.Equal(1, removed.GetProperty("summary").GetProperty("columnsRemoved").GetInt32());
        Assert.False(removed.GetProperty("redacted").GetBoolean());
    }

    [Fact]
    public async Task SystemRules_AreAdministratorOnly_FireOnTheOrganizationsOwnSignals_AndNeverCrossOrganizations()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        await CreateSignInTableAsync("SysPortalSignIns");
        var (applicationId, _, _) = await PrepareApplicationAsync(alice, "sysrules", "SysPortalSignIns");
        var (_, managerEmail) = await InsertUserAsync(_db.Data.OrganizationAId, await RoleIdAsync(alice, "Management"), "System Manager");
        using var manager = await LoginAsync(managerEmail);

        // Templates: system templates are offered to administrators only; Management cannot create system rules.
        var adminTemplates = (await (await alice.GetAsync("/api/monitoring/templates")).ReadJsonAsync()).EnumerateArray().ToList();
        foreach (var code in new[] { RuleTemplates.SourceConnectionFailures, RuleTemplates.EmailDeliveryFailures, RuleTemplates.AiBudgetReached, RuleTemplates.MetadataDrift, RuleTemplates.ReportDeliveryFailures })
        {
            var template = Assert.Single(adminTemplates, t => t.GetProperty("code").GetString() == code);
            Assert.Equal("System", template.GetProperty("kind").GetString());
            Assert.True(template.GetProperty("available").GetBoolean());
        }

        var managerTemplates = (await (await manager.GetAsync("/api/monitoring/templates")).ReadJsonAsync()).EnumerateArray().ToList();
        Assert.False(managerTemplates.Single(t => t.GetProperty("code").GetString() == RuleTemplates.EmailDeliveryFailures).GetProperty("available").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostJsonAsync("/api/monitoring/rules", SystemRuleBody("Manager system rule", RuleTemplates.EmailDeliveryFailures, "email_failures", 0))).StatusCode);
        var badSignal = await alice.PostJsonAsync("/api/monitoring/rules", SystemRuleBody("Bad signal", null, "disk_space", 0));
        Assert.Equal("rule_signal_invalid", (await badSignal.ReadJsonAsync()).GetProperty("detail").GetString());
        var badBudget = await alice.PostJsonAsync("/api/monitoring/rules", SystemRuleBody("Bad budget threshold", RuleTemplates.AiBudgetReached, "ai_budget", 1500));
        Assert.Equal("rule_threshold_invalid", (await badBudget.ReadJsonAsync()).GetProperty("detail").GetString());

        // Signals of organization A, plus one failed email of organization B that A must never count.
        var now = DateTimeOffset.UtcNow;
        await using (var context = _db.CreateContext(TestData.SystemPrincipal))
        {
            foreach (var (organizationId, count) in new[] { (_db.Data.OrganizationAId, 3), (_db.Data.OrganizationBId, 1) })
            {
                for (var i = 0; i < count; i++)
                {
                    context.EmailMessages.Add(new EmailMessageRecord
                    {
                        EmailMessageId = Ids.New(), OrganizationId = organizationId, RecipientEmail = $"bounce{i}@org.test", RecipientName = "Bounce", Subject = "Digest",
                        TemplateCode = "Monitoring.Digest", Status = EmailDeliveryStatus.Failed, Attempts = 5, LastError = "smtp_rejected", RequestedByType = PrincipalType.OrganizationUser, CreatedAt = now.AddMinutes(-5 - i),
                    });
                }
            }

            for (var i = 0; i < 2; i++)
            {
                var runId = Ids.New();
                // The month's AI spending is read from the AI usage ledger (P13.48).
                context.AiCalls.Add(new AiCall
                {
                    CallId = Ids.New(), OrganizationId = _db.Data.OrganizationAId, UserId = _db.Data.UserA1Id, RunId = runId, Purpose = AiCallPurposes.Plan, ProviderType = "Anthropic", Model = "test-model",
                    InputCost = 40m, OutputCost = 5m, TotalCost = 45m, Succeeded = true, CreatedAt = now.AddMinutes(-3 - i),
                });
                context.AnalysisExecutionMetrics.Add(new AnalysisExecutionMetric
                {
                    MetricId = runId, OrganizationId = _db.Data.OrganizationAId, UserId = _db.Data.UserA1Id, ApplicationId = applicationId, Status = AnalysisStatus.Failed,
                    FailureStage = "execute", FailureCode = "source_unreachable", EstimatedCost = 45m, CreatedAt = now.AddMinutes(-3 - i),
                });
            }

            await context.SaveChangesAsync();
        }

        // Email failures: three in A (B's is not counted).
        var emailRule = await CreateAsync(alice, SystemRuleBody("Email failures", RuleTemplates.EmailDeliveryFailures, "email_failures", 0));
        await RunRuleAsync(emailRule);
        var emailAlert = Assert.Single((await (await alice.GetAsync($"/api/monitoring/alerts?ruleId={emailRule}")).ReadJsonAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal(await FailedEmailsAsync(_db.Data.OrganizationAId), emailAlert.GetProperty("summary").GetProperty("measure").GetDecimal());

        // Connection failures: the application with failed executions.
        var connectionRule = await CreateAsync(alice, SystemRuleBody("Source failures", RuleTemplates.SourceConnectionFailures, "connection_failures", 1));
        await RunRuleAsync(connectionRule);
        var connectionAlert = Assert.Single((await (await alice.GetAsync($"/api/monitoring/alerts?ruleId={connectionRule}")).ReadJsonAsync()).GetProperty("items").EnumerateArray(), a => a.GetProperty("groupKey").GetString() == "SYSRULES");
        Assert.Equal(2, connectionAlert.GetProperty("summary").GetProperty("failedQueries").GetInt32());

        // AI budget: skipped (rule stays active) until a budget is set; fires at 80 % once it is.
        var budgetRule = await CreateAsync(alice, SystemRuleBody("AI budget", RuleTemplates.AiBudgetReached, "ai_budget", 80));
        await RunRuleAsync(budgetRule);
        var skipped = await (await alice.GetAsync($"/api/monitoring/rules/{budgetRule}")).ReadJsonAsync();
        Assert.True(skipped.GetProperty("isActive").GetBoolean());
        Assert.Equal("Skipped", skipped.GetProperty("lastOutcome").GetString());
        Assert.Equal("ai_budget_not_set", skipped.GetProperty("lastError").GetString());

        var settings = await (await alice.GetAsync("/api/monitoring/settings")).ReadJsonAsync();
        Assert.True(settings.GetProperty("aiSpendThisMonth").GetDecimal() >= 90m);
        object SettingsBody(decimal? budget) => new
        {
            digestTime = settings.GetProperty("digestTime").GetString(), workingHoursStart = settings.GetProperty("workingHoursStart").GetString(), workingHoursEnd = settings.GetProperty("workingHoursEnd").GetString(),
            workingDays = settings.GetProperty("workingDays").GetString(), retentionMonths = settings.GetProperty("retentionMonths").GetInt32(), monthlyAiBudget = budget,
        };
        var invalid = await alice.PutAsJsonAsync("/api/monitoring/settings", SettingsBody(-5));
        Assert.Equal("settings_ai_budget_invalid", (await invalid.ReadJsonAsync()).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsJsonAsync("/api/monitoring/settings", SettingsBody(100))).StatusCode);
        var updated = await alice.PutAsJsonAsync("/api/monitoring/settings", SettingsBody(100));
        Assert.True(updated.StatusCode == HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        Assert.Equal(100m, (await updated.ReadJsonAsync()).GetProperty("monthlyAiBudget").GetDecimal());
        await RunRuleAsync(budgetRule);
        var budgetAlert = Assert.Single((await (await alice.GetAsync($"/api/monitoring/alerts?ruleId={budgetRule}")).ReadJsonAsync()).GetProperty("items").EnumerateArray());
        Assert.True(budgetAlert.GetProperty("summary").GetProperty("measure").GetDecimal() >= 90m);
        Assert.Equal(100m, budgetAlert.GetProperty("summary").GetProperty("budget").GetDecimal());

        // Organization B: its own email rule counts B's one failure only, and B never sees A's system alerts.
        var (_, bAdminEmail) = await InsertUserAsync(_db.Data.OrganizationBId, _db.Data.RoleAdminBId, "B System Admin");
        using var bAdmin = await LoginAsync(bAdminEmail);
        var bRule = await CreateAsync(bAdmin, SystemRuleBody("B email failures", RuleTemplates.EmailDeliveryFailures, "email_failures", 0));
        await RunRuleAsync(bRule);
        var bAlert = Assert.Single((await (await bAdmin.GetAsync($"/api/monitoring/alerts?ruleId={bRule}")).ReadJsonAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal(await FailedEmailsAsync(_db.Data.OrganizationBId), bAlert.GetProperty("summary").GetProperty("measure").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, (await bAdmin.GetAsync($"/api/monitoring/rules/{budgetRule}")).StatusCode);
        Assert.DoesNotContain((await (await bAdmin.GetAsync("/api/monitoring/alerts")).ReadJsonAsync()).GetProperty("items").EnumerateArray(), a => a.GetProperty("ruleId").GetGuid() == emailRule);
    }

    private async Task<decimal> FailedEmailsAsync(Guid organizationId)
    {
        await using var context = _db.CreateContext(TestData.SystemPrincipal);
        var since = DateTimeOffset.UtcNow.AddHours(-24);
        return await context.EmailMessages.CountAsync(e => e.OrganizationId == organizationId && e.Status == EmailDeliveryStatus.Failed && e.CreatedAt >= since);
    }

    private static string DraftJson() => """
        {"expressible":true,"reason":null,"name":"نفس الحساب من أكثر من جهاز","object":{"schema":"dbo","object":"DraftPortalSignIns"},
         "timeColumn":"LoginAt","windowHours":24,"groupBy":["AccountNumber"],"measure":{"aggregate":"count_distinct","column":"DeviceId"},"filters":[],
         "condition":{"operator":"gt","value":1},"comparison":"none","severity":"High","recurrence":"Daily","time":"06:00","dayOfWeek":null,"intervalMinutes":null,
         "explanation":"يتحقق يومياً من الحسابات التي استُخدمت من أكثر من جهاز خلال آخر 24 ساعة.","assumptions":["النافذة 24 ساعة تنتهي وقت التشغيل."]}
        """;

    private sealed record RuleRequest(string name, string? description, string kind, string? templateCode, object definition, string severity, string recurrenceKind, int intervalMinutes, string timeOfDay, int? dayOfWeek, int cooldownHours, bool isActive, Guid? ownerUserId, object[] recipients);

    private static RuleRequest DataRuleBody(string name, Guid objectId, Guid account, Guid time, Guid? device) => new(
        name, null, "Data", null,
        new { data = new { objectId, timeColumnId = time, windowHours = 24, groupByColumnIds = new[] { account }, measure = device is { } d ? (object)new { aggregate = "count_distinct", columnId = d } : new { aggregate = "count" }, filters = Array.Empty<object>(), condition = new { @operator = "gt", value = 1 }, comparison = "none", maxGroups = 200 } },
        "Medium", "Daily", 0, "06:00:00", null, 24, true, null, []);

    private static RuleRequest SystemRuleBody(string name, string? templateCode, string signal, decimal threshold) => new(
        name, null, "System", templateCode, new { system = new { signal, windowHours = 24, threshold } },
        "Medium", "Daily", 0, "08:00:00", null, 24, true, null, []);

    private static async Task<Guid> CreateAsync(HttpClient client, RuleRequest body)
    {
        var created = await client.PostJsonAsync("/api/monitoring/rules", body);
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.ReadJsonAsync()).GetProperty("ruleId").GetGuid();
    }

    private static async Task<Guid> RoleIdAsync(HttpClient admin, string code)
        => (await (await admin.GetAsync("/api/roles")).ReadJsonAsync()).EnumerateArray().Single(r => r.GetProperty("roleCode").GetString() == code).GetProperty("roleId").GetGuid();

    private static async Task SetRolesAsync(HttpClient alice, Func<string, Guid> column)
    {
        foreach (var (name, role) in new[] { ("AccountNumber", "AccountIdentifier"), ("DeviceId", "DeviceIdentifier"), ("LoginAt", "Timestamp") })
        {
            var put = await alice.PutAsJsonAsync($"/api/metadata/columns/{column(name)}", new { displayNameEn = name, displayNameAr = (string?)null, businessDescription = (string?)null, classification = "Confidential", isApproved = true, semanticRole = role });
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        }
    }

    private async Task RunRuleAsync(Guid ruleId)
    {
        await using (var context = _db.CreateContext(TestData.SystemPrincipal))
        {
            var job = await context.ScheduledJobs.SingleAsync(j => j.JobType == ScheduledJobType.MonitoringRule && j.TargetId == ruleId);
            job.NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            job.IsEnabled = true;
            await context.SaveChangesAsync();
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextInitializer>().Initialize(ScheduledJobRunner.SchedulerPrincipal);
        Assert.True(await scope.ServiceProvider.GetRequiredService<ScheduledJobRunner>().RunDueJobsAsync("nl-system-tests") >= 1);
    }

    private async Task CreateSignInTableAsync(string table)
    {
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, JobRecurrence.ResolveTimeZone("Asia/Dubai")).DateTime;
        await ExecuteAsync($"""
            IF OBJECT_ID(N'dbo.{table}') IS NOT NULL DROP TABLE dbo.{table};
            CREATE TABLE dbo.{table} (SignInId int IDENTITY PRIMARY KEY, AccountNumber nvarchar(20) NOT NULL, DeviceId nvarchar(40) NOT NULL, LoginAt datetime2 NOT NULL);
            INSERT INTO dbo.{table} (AccountNumber, DeviceId, LoginAt) VALUES
                (N'ACC-1', N'DEV-A', DATEADD(hour, -1, @now)), (N'ACC-1', N'DEV-B', DATEADD(hour, -2, @now)), (N'ACC-1', N'DEV-C', DATEADD(hour, -3, @now)),
                (N'ACC-2', N'DEV-D', DATEADD(hour, -1, @now)), (N'ACC-2', N'DEV-D', DATEADD(hour, -5, @now));
            """, local);
    }

    private async Task ExecuteAsync(string sql, DateTime? now = null)
    {
        await using var connection = new SqlConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (now is { } value)
        {
            command.Parameters.AddWithValue("@now", value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private async Task<HttpClient> LoginAsync(string email, string password = TestData.UserPassword)
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, password)).StatusCode);
        return client;
    }

    private async Task<(Guid ApplicationId, Guid ObjectId, List<JsonElement> Columns)> PrepareApplicationAsync(HttpClient alice, string tag, string objectName)
    {
        var source = new SqlConnectionStringBuilder(_db.ConnectionString) { WorkstationID = tag }.ConnectionString;
        var created = await (await alice.PostJsonAsync("/api/applications", new { applicationCode = tag, applicationName = tag, applicationNameAr = tag, databaseType = "SqlServer", connectionString = source })).ReadJsonAsync();
        var applicationId = created.GetProperty("applicationId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/applications/{applicationId}/test-connection", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/applications/{applicationId}/approval", new { isApproved = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/metadata/applications/{applicationId}/refresh", new { })).StatusCode);
        var objects = await (await alice.GetAsync($"/api/metadata/applications/{applicationId}/objects?search={objectName}&pageSize=10")).ReadJsonAsync();
        var objectId = objects.GetProperty("items").EnumerateArray().Single(o => o.GetProperty("schemaName").GetString() == "dbo" && o.GetProperty("objectName").GetString() == objectName).GetProperty("objectId").GetGuid();
        var approved = await (await alice.PostJsonAsync($"/api/metadata/objects/{objectId}/approve", new { approve = true })).ReadJsonAsync();
        return (applicationId, objectId, approved.GetProperty("columns").EnumerateArray().ToList());
    }

    private async Task<(Guid UserId, string Email)> InsertUserAsync(Guid organizationId, Guid? roleId, string fullName)
    {
        var email = $"nlsys.{Guid.NewGuid():N}@org.test";
        var userId = Ids.New();
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var now = DateTimeOffset.UtcNow;
        context.OrganizationUsers.Add(TestData.NewUser(userId, organizationId, email, fullName));
        context.LoginIdentities.Add(TestData.Identity(email, PrincipalType.OrganizationUser, userId, organizationId, now));
        if (roleId is { } r)
        {
            context.UserRoles.Add(new UserRole { OrganizationId = organizationId, UserId = userId, RoleId = r, AssignedAt = now, AssignedByAdministratorId = _db.Data.AdministratorId });
        }

        await context.SaveChangesAsync();
        return (userId, email);
    }
}
