using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Metadata;
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
/// P14.2 Monitoring Rules and Alerts (specification §35.1–35.2): the owner's example ("same account used from more
/// than one device in a day") fires on a real table through the secure query path under the owner's permissions;
/// recipients without permission see titles only; cooldown counts repeats; the digest arrives once per recipient;
/// access rules flag users and suspension is a human, audited action; organizations never see each other.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class MonitoringRulesTests : IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly AiApiFactory _factory;
    private readonly string _keyRing = Path.Combine(Path.GetTempPath(), "wi-keys-tests", "monitoring-rules-tests");

    public MonitoringRulesTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new AiApiFactory(db.ConnectionString, new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = _keyRing, ["Ai:Anthropic:ApiKey"] = "sk-ant-seeded-platform-key-0003" });
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task MultiDeviceRule_FiresAsTheOwner_RedactsForRecipientsWithoutPermission_CountsRepeats_AndDigestsOnce()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        await CreateSignInTableAsync();
        var (applicationId, objectId, columns) = await PrepareApplicationAsync(alice, "rules", "MonitorPortalSignIns");
        Guid Column(string name) => columns.Single(c => c.GetProperty("columnName").GetString() == name).GetProperty("columnId").GetGuid();
        foreach (var (name, role) in new[] { ("AccountNumber", "AccountIdentifier"), ("DeviceId", "DeviceIdentifier"), ("LoginAt", "Timestamp") })
        {
            var put = await alice.PutAsJsonAsync($"/api/metadata/columns/{Column(name)}", new { displayNameEn = name, displayNameAr = (string?)null, businessDescription = (string?)null, classification = "Confidential", isApproved = true, semanticRole = role });
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        }

        var roles = (await (await alice.GetAsync("/api/roles")).ReadJsonAsync()).EnumerateArray().ToList();
        Guid RoleId(string code) => roles.Single(r => r.GetProperty("roleCode").GetString() == code).GetProperty("roleId").GetGuid();
        var (managerId, managerEmail) = await InsertUserAsync(_db.Data.OrganizationAId, RoleId("Management"), "Cyber Manager");
        var (analystId, analystEmail) = await InsertUserAsync(_db.Data.OrganizationAId, RoleId("Analyst"), "Cyber Analyst");
        using var manager = await LoginAsync(managerEmail);
        using var analyst = await LoginAsync(analystEmail);

        // 1. Templates follow the owner's metadata and permissions: the manager can read the table, the administrator role alone cannot.
        var forManager = (await (await alice.GetAsync($"/api/monitoring/templates?ownerUserId={managerId}")).ReadJsonAsync()).EnumerateArray().ToList();
        var multiDevice = forManager.Single(t => t.GetProperty("code").GetString() == RuleTemplates.MultiDeviceSameAccount);
        Assert.True(multiDevice.GetProperty("available").GetBoolean());
        var candidate = Assert.Single(multiDevice.GetProperty("candidates").EnumerateArray(), c => c.GetProperty("objectId").GetGuid() == objectId);
        Assert.Equal(Column("DeviceId"), candidate.GetProperty("slots").GetProperty("device")[0].GetProperty("columnId").GetGuid());
        var forAdmin = (await (await alice.GetAsync("/api/monitoring/templates")).ReadJsonAsync()).EnumerateArray().ToList();
        Assert.False(forAdmin.Single(t => t.GetProperty("code").GetString() == RuleTemplates.MultiDeviceSameAccount).GetProperty("available").GetBoolean());
        Assert.True(forAdmin.Single(t => t.GetProperty("code").GetString() == RuleTemplates.RepeatedUnauthorizedAttempts).GetProperty("available").GetBoolean());

        // 2. The administrator creates the owner's example for the manager, with the analyst as a recipient.
        var rule = RuleBody("Same account, several devices", managerId, objectId, Column("AccountNumber"), Column("DeviceId"), Column("LoginAt"), recipients: [new { userId = analystId, roleId = (Guid?)null }]);
        var created = await alice.PostJsonAsync("/api/monitoring/rules", rule);
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var ruleJson = await created.ReadJsonAsync();
        var ruleId = ruleJson.GetProperty("ruleId").GetGuid();
        Assert.Equal("Cyber Manager", ruleJson.GetProperty("ownerName").GetString());
        Assert.Equal("dbo.MonitorPortalSignIns", ruleJson.GetProperty("objectName").GetString());
        Assert.True(ruleJson.TryGetProperty("nextRunAt", out _));

        // 3. Test as the owner: ACC-1 used three devices, ACC-2 one, ACC-3 two (one of them yesterday, outside the window).
        var managerTest = await (await manager.PostAsync($"/api/monitoring/rules/{ruleId}/test", null)).ReadJsonAsync();
        Assert.Equal("Fired", managerTest.GetProperty("outcome").GetString());
        Assert.False(managerTest.GetProperty("redacted").GetBoolean());
        var match = Assert.Single(managerTest.GetProperty("matches").EnumerateArray());
        Assert.Equal("ACC-1", match.GetProperty("groupKey").GetString());
        Assert.Equal(3, match.GetProperty("measure").GetDecimal());
        // The administrator may run it but, holding no data permission, sees counts only.
        var adminTest = await (await alice.PostAsync($"/api/monitoring/rules/{ruleId}/test", null)).ReadJsonAsync();
        Assert.True(adminTest.GetProperty("redacted").GetBoolean());
        Assert.Equal(1, adminTest.GetProperty("matchCount").GetInt32());
        Assert.Empty(adminTest.GetProperty("matches").EnumerateArray());

        // 4. Scheduled run: one High alert, in-app notification to owner and recipient; run again: the same alert counts the repeat.
        await RunRuleAsync(ruleId);
        await RunRuleAsync(ruleId);
        await using (var context = _db.CreateContext(TestData.SystemPrincipal))
        {
            var alert = Assert.Single(await context.Alerts.IgnoreQueryFilters().Where(a => a.RuleId == ruleId).ToListAsync());
            Assert.Equal(_db.Data.OrganizationAId, alert.OrganizationId);
            Assert.Equal("ACC-1", alert.GroupKey);
            Assert.Equal(2, alert.Occurrences);
            Assert.Equal(AlertSeverity.High, alert.Severity);
            var events = await context.AlertEvents.IgnoreQueryFilters().Where(e => e.AlertId == alert.AlertId).Select(e => e.EventType).ToListAsync();
            Assert.Equal([AlertEventType.Raised, AlertEventType.Repeated], events.Order());
            Assert.Equal(2, await context.RuleEvaluations.IgnoreQueryFilters().CountAsync(e => e.RuleId == ruleId && !e.IsTest));
            Assert.Equal(2, await context.RuleEvaluations.IgnoreQueryFilters().CountAsync(e => e.RuleId == ruleId && e.IsTest));
            var notified = await context.Notifications.IgnoreQueryFilters().Where(n => n.NotificationType == "Monitoring.Alert" && n.EntityId == alert.AlertId.ToString()).Select(n => n.RecipientId).ToListAsync();
            Assert.Equal(new[] { managerId, analystId }.Order(), notified.Order());
            var data = await context.Notifications.IgnoreQueryFilters().Where(n => n.NotificationType == "Monitoring.Alert" && n.EntityId == alert.AlertId.ToString()).Select(n => n.DataJson).FirstAsync();
            Assert.DoesNotContain("ACC-1", data, StringComparison.Ordinal);
        }

        // 5. Alert centre: the owner sees the account and the device count; the analyst (no permission on the table) sees the title only.
        var managerAlerts = await (await manager.GetAsync("/api/monitoring/alerts")).ReadJsonAsync();
        var managerAlert = Assert.Single(managerAlerts.GetProperty("items").EnumerateArray(), a => a.GetProperty("ruleId").GetGuid() == ruleId);
        Assert.Equal("ACC-1", managerAlert.GetProperty("groupKey").GetString());
        Assert.Equal(3, managerAlert.GetProperty("summary").GetProperty("measure").GetDecimal());
        var analystAlert = Assert.Single((await (await analyst.GetAsync("/api/monitoring/alerts")).ReadJsonAsync()).GetProperty("items").EnumerateArray(), a => a.GetProperty("ruleId").GetGuid() == ruleId);
        Assert.True(analystAlert.GetProperty("redacted").GetBoolean());
        // The API omits nulls: no group key and no summary reach the analyst.
        Assert.False(analystAlert.TryGetProperty("groupKey", out _));
        Assert.False(analystAlert.TryGetProperty("summary", out _));
        Assert.Equal("Same account, several devices", analystAlert.GetProperty("title").GetString());
        // Analysts cannot manage rules; they still act on alerts they receive.
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.GetAsync("/api/monitoring/rules")).StatusCode);
        var alertId = analystAlert.GetProperty("alertId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await analyst.PostJsonAsync($"/api/monitoring/alerts/{alertId}/acknowledge", new { note = "Looking" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.PostJsonAsync($"/api/monitoring/alerts/{alertId}/suspend-user", new { note = "x" })).StatusCode);

        // 6. Daily digest: one email per recipient, redacted for the analyst; no raw rows.
        await RunDigestAsync(_db.Data.OrganizationAId);
        var managerMail = await _factory.Emails.WaitForAsync(managerEmail, m => m.Subject.Contains("digest", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("ACC-1", managerMail.TextBody, StringComparison.Ordinal);
        var analystMail = await _factory.Emails.WaitForAsync(analystEmail, m => m.Subject.Contains("digest", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("ACC-1", analystMail.TextBody, StringComparison.Ordinal);
        Assert.Contains("Details hidden", analystMail.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain(_factory.Emails.Messages, m => m.ToEmail == TestData.AliceEmail && m.Subject.Contains("digest", StringComparison.OrdinalIgnoreCase));
        Assert.Single(_factory.Emails.Messages, m => m.ToEmail == managerEmail && m.Subject.Contains("digest", StringComparison.OrdinalIgnoreCase));

        // 7. Management users manage their own data rules only.
        var own = await manager.PostJsonAsync("/api/monitoring/rules", RuleBody("Manager's own rule", null, objectId, Column("AccountNumber"), Column("DeviceId"), Column("LoginAt")));
        Assert.Equal(HttpStatusCode.Created, own.StatusCode);
        Assert.Equal(managerId, (await own.ReadJsonAsync()).GetProperty("ownerUserId").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostJsonAsync("/api/monitoring/rules", RuleBody("For someone else", _db.Data.UserA1Id, objectId, Column("AccountNumber"), Column("DeviceId"), Column("LoginAt")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsJsonAsync($"/api/monitoring/rules/{ruleId}", RuleBody("Edited", managerId, objectId, Column("AccountNumber"), Column("DeviceId"), Column("LoginAt")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostJsonAsync("/api/monitoring/rules", AccessRuleBody("Manager access rule"))).StatusCode);
        // An owner without permission on the object is refused at save time: a rule can never read beyond its owner.
        var refused = await alice.PostJsonAsync("/api/monitoring/rules", RuleBody("Owned by the administrator", _db.Data.UserA1Id, objectId, Column("AccountNumber"), Column("DeviceId"), Column("LoginAt")));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("rule_not_authorized", (await refused.ReadJsonAsync()).GetProperty("detail").GetString());

        // 8. Organization B and the Platform Administrator see nothing of organization A.
        var (_, bAdminEmail) = await InsertUserAsync(_db.Data.OrganizationBId, _db.Data.RoleAdminBId, "B Admin");
        using var bAdmin = await LoginAsync(bAdminEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await bAdmin.GetAsync($"/api/monitoring/rules/{ruleId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bAdmin.GetAsync($"/api/monitoring/alerts/{alertId}")).StatusCode);
        Assert.DoesNotContain((await (await bAdmin.GetAsync("/api/monitoring/alerts")).ReadJsonAsync()).GetProperty("items").EnumerateArray(), a => a.GetProperty("alertId").GetGuid() == alertId);
        Assert.Equal(HttpStatusCode.NotFound, (await bAdmin.PostJsonAsync($"/api/monitoring/alerts/{alertId}/resolve", new { note = "not mine" })).StatusCode);
        using var platform = await LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync("/api/monitoring/alerts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync("/api/monitoring/rules")).StatusCode);
        var summary = (await (await platform.GetAsync("/api/platform/monitoring/summary")).ReadJsonAsync()).EnumerateArray().ToList();
        var orgA = Assert.Single(summary, s => s.GetProperty("organizationId").GetGuid() == _db.Data.OrganizationAId);
        Assert.True(orgA.GetProperty("openAlerts").GetInt32() >= 1);
        Assert.DoesNotContain("ACC-1", JsonSerializer.Serialize(summary), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccessRule_FlagsTheUser_StatusChangesNeedNotes_AndSuspensionIsAHumanAuditedAction()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        var roles = (await (await alice.GetAsync("/api/roles")).ReadJsonAsync()).EnumerateArray().ToList();
        var analystRole = roles.Single(r => r.GetProperty("roleCode").GetString() == "Analyst").GetProperty("roleId").GetGuid();
        var (suspectId, suspectEmail) = await InsertUserAsync(_db.Data.OrganizationAId, analystRole, "Curious Analyst");
        var now = DateTimeOffset.UtcNow;
        await using (var context = _db.CreateContext(TestData.SystemPrincipal))
        {
            for (var i = 0; i < 4; i++)
            {
                context.AnalysisAccessFacts.Add(new AnalysisAccessFact
                {
                    FactId = Ids.New(), OrganizationId = _db.Data.OrganizationAId, UserId = suspectId, RoleCodes = "Analyst", ApplicationId = Ids.New(), MetricId = Ids.New(),
                    SchemaName = "dbo", ObjectName = "Payslips", IsPrimaryObject = true, AccessedAt = now.AddMinutes(-10 - i), HighestClassification = DataClassification.Restricted,
                    ColumnIdsJson = "[]", FilterColumnIdsJson = "[]", Outcome = AccessOutcome.Unauthorized, FailureCode = "object_not_permitted",
                });
            }

            await context.SaveChangesAsync();
        }

        var created = await alice.PostJsonAsync("/api/monitoring/rules", AccessRuleBody("Repeated unauthorized attempts"));
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var ruleId = (await created.ReadJsonAsync()).GetProperty("ruleId").GetGuid();
        await RunRuleAsync(ruleId);

        var page = await (await alice.GetAsync($"/api/monitoring/alerts?ruleId={ruleId}")).ReadJsonAsync();
        var alert = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(suspectId, alert.GetProperty("subjectUserId").GetGuid());
        Assert.Equal("Repeated unauthorized attempts — Curious Analyst", alert.GetProperty("title").GetString());
        Assert.Equal(4, alert.GetProperty("summary").GetProperty("measure").GetDecimal());
        Assert.True(alert.GetProperty("canSuspendSubject").GetBoolean());
        var alertId = alert.GetProperty("alertId").GetGuid();

        // Resolving or dismissing needs a note; an invalid transition is a conflict.
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostJsonAsync($"/api/monitoring/alerts/{alertId}/dismiss", new { note = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PostJsonAsync($"/api/monitoring/alerts/{alertId}/reopen", new { note = "x" })).StatusCode);

        // D2: nothing happened to the user until a person decided.
        await using (var context = _db.CreateContext(TestData.SystemPrincipal))
        {
            Assert.Equal(OrganizationUserStatus.Active, (await context.OrganizationUsers.IgnoreQueryFilters().SingleAsync(u => u.UserId == suspectId)).Status);
        }

        var suspended = await alice.PostJsonAsync($"/api/monitoring/alerts/{alertId}/suspend-user", new { note = "Confirmed with the line manager." });
        Assert.True(suspended.StatusCode == HttpStatusCode.OK, await suspended.Content.ReadAsStringAsync());
        var detail = await suspended.ReadJsonAsync();
        Assert.Contains(detail.GetProperty("events").EnumerateArray(), e => e.GetProperty("eventType").GetString() == "UserSuspended" && e.GetProperty("actorName").GetString() == "Alice A");
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/monitoring/alerts/{alertId}/resolve", new { note = "User suspended pending review." })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().LoginAsync(suspectEmail, TestData.UserPassword)).StatusCode);

        await using (var context = _db.CreateContext(TestData.SystemPrincipal))
        {
            Assert.Equal(OrganizationUserStatus.Inactive, (await context.OrganizationUsers.IgnoreQueryFilters().SingleAsync(u => u.UserId == suspectId)).Status);
            Assert.True(await context.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.EventType == "Alert.UserSuspended" && a.EntityId == alertId.ToString()));
            Assert.True(await context.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.EventType == "Alert.StatusChanged" && a.EntityId == alertId.ToString()));
            // Append-only: evaluations and alert events cannot be edited.
            var evaluation = await context.RuleEvaluations.IgnoreQueryFilters().FirstAsync(e => e.RuleId == ruleId);
            evaluation.MatchCount = 99;
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            context.ChangeTracker.Clear();
            context.AlertEvents.Remove(await context.AlertEvents.IgnoreQueryFilters().FirstAsync(e => e.AlertId == alertId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task RuleWhoseOwnerLeft_IsPausedAndFlagged_NotRunSilently()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        var roles = (await (await alice.GetAsync("/api/roles")).ReadJsonAsync()).EnumerateArray().ToList();
        var analystRole = roles.Single(r => r.GetProperty("roleCode").GetString() == "Analyst").GetProperty("roleId").GetGuid();
        var (ownerId, _) = await InsertUserAsync(_db.Data.OrganizationAId, analystRole, "Leaving Owner");
        var body = AccessRuleBody("Owner leaves");
        var created = await alice.PostJsonAsync("/api/monitoring/rules", body with { ownerUserId = ownerId });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var ruleId = (await created.ReadJsonAsync()).GetProperty("ruleId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/users/{ownerId}/status", new { status = "Inactive" })).StatusCode);

        await RunRuleAsync(ruleId);
        var rule = await (await alice.GetAsync($"/api/monitoring/rules/{ruleId}")).ReadJsonAsync();
        Assert.False(rule.GetProperty("isActive").GetBoolean());
        Assert.Equal("rule_owner_inactive", rule.GetProperty("lastError").GetString());
        Assert.False(rule.TryGetProperty("nextRunAt", out _));
        var flagged = Assert.Single((await (await alice.GetAsync($"/api/monitoring/alerts?ruleId={ruleId}")).ReadJsonAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal("rule_invalid", flagged.GetProperty("groupKey").GetString());
        Assert.Equal("rule_owner_inactive", flagged.GetProperty("summary").GetProperty("reason").GetString());
        // Resuming re-validates the owner.
        var resume = await alice.PostAsync($"/api/monitoring/rules/{ruleId}/resume", null);
        Assert.Equal(HttpStatusCode.BadRequest, resume.StatusCode);
        Assert.Equal("rule_owner_invalid", (await resume.ReadJsonAsync()).GetProperty("detail").GetString());
    }

    private sealed record RuleRequest(string name, string? description, string kind, string? templateCode, object definition, string severity, string recurrenceKind, int intervalMinutes, string timeOfDay, int? dayOfWeek, int cooldownHours, bool isActive, Guid? ownerUserId, object[] recipients);

    private static RuleRequest RuleBody(string name, Guid? owner, Guid objectId, Guid account, Guid device, Guid time, object[]? recipients = null) => new(
        name, "Owner's example", "Data", RuleTemplates.MultiDeviceSameAccount,
        new { data = new { objectId, timeColumnId = time, windowHours = 24, groupByColumnIds = new[] { account }, measure = new { aggregate = "count_distinct", columnId = device }, filters = Array.Empty<object>(), condition = new { @operator = "gt", value = 1 }, comparison = "none", maxGroups = 200 } },
        "High", "Daily", 0, "06:00:00", null, 24, true, owner, recipients ?? []);

    private static RuleRequest AccessRuleBody(string name) => new(
        name, null, "Access", RuleTemplates.RepeatedUnauthorizedAttempts,
        new { access = new { signal = "unauthorized", windowHours = 24, objectIds = Array.Empty<Guid>(), roleCodes = Array.Empty<string>(), outsideWorkingHours = false, threshold = new { mode = "absolute", value = 2 } } },
        "High", "Interval", 60, "00:00:00", null, 24, true, null, []);

    private async Task RunRuleAsync(Guid ruleId)
    {
        await using (var context = _db.CreateContext(TestData.SystemPrincipal))
        {
            var job = await context.ScheduledJobs.SingleAsync(j => j.JobType == ScheduledJobType.MonitoringRule && j.TargetId == ruleId);
            job.NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            job.IsEnabled = true;
            await context.SaveChangesAsync();
        }

        await RunDueAsync();
    }

    private async Task RunDigestAsync(Guid organizationId)
    {
        await using (var scope = SchedulerScope())
        {
            await scope.ServiceProvider.GetRequiredService<ScheduledJobRunner>().EnsureStandingJobsAsync();
        }

        await using (var context = _db.CreateContext(TestData.SystemPrincipal))
        {
            var job = await context.ScheduledJobs.SingleAsync(j => j.OrganizationId == organizationId && j.JobType == ScheduledJobType.AlertDigest);
            job.NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            job.LastRunAt = null;
            await context.SaveChangesAsync();
        }

        await RunDueAsync();
    }

    private async Task RunDueAsync()
    {
        await using var scope = SchedulerScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<ScheduledJobRunner>().RunDueJobsAsync("monitoring-tests") >= 1);
        await using var context = _db.CreateContext(TestData.SystemPrincipal);
        var failed = await context.ScheduledJobs.Where(j => j.LastOutcome == JobOutcome.Failed && j.LastRunAt > DateTimeOffset.UtcNow.AddMinutes(-1)).Select(j => j.LastError).ToListAsync();
        Assert.Empty(failed);
    }

    private AsyncServiceScope SchedulerScope()
    {
        var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextInitializer>().Initialize(ScheduledJobRunner.SchedulerPrincipal);
        return scope;
    }

    private async Task CreateSignInTableAsync()
    {
        // Organization A runs on Asia/Dubai: the rows are written in its local time, as a business system would.
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, JobRecurrence.ResolveTimeZone("Asia/Dubai")).DateTime;
        await using var connection = new SqlConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF OBJECT_ID(N'dbo.MonitorPortalSignIns') IS NULL
                CREATE TABLE dbo.MonitorPortalSignIns (SignInId int IDENTITY PRIMARY KEY, AccountNumber nvarchar(20) NOT NULL, DeviceId nvarchar(40) NOT NULL, LoginAt datetime2 NOT NULL);
            DELETE FROM dbo.MonitorPortalSignIns;
            INSERT INTO dbo.MonitorPortalSignIns (AccountNumber, DeviceId, LoginAt) VALUES
                (N'ACC-1', N'DEV-A', DATEADD(hour, -1, @now)), (N'ACC-1', N'DEV-B', DATEADD(hour, -2, @now)), (N'ACC-1', N'DEV-C', DATEADD(hour, -3, @now)), (N'ACC-1', N'DEV-A', DATEADD(hour, -4, @now)),
                (N'ACC-2', N'DEV-D', DATEADD(hour, -1, @now)), (N'ACC-2', N'DEV-D', DATEADD(hour, -5, @now)),
                (N'ACC-3', N'DEV-E', DATEADD(hour, -2, @now)), (N'ACC-3', N'DEV-F', DATEADD(hour, -30, @now));
            """;
        command.Parameters.AddWithValue("@now", local);
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
        var email = $"monitor.{Guid.NewGuid():N}@org.test";
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
