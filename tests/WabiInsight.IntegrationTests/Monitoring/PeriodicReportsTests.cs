using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WabiInsight.Core.Common;
using WabiInsight.Core.Email;
using WabiInsight.Core.Entities.Monitoring;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Monitoring;
using WabiInsight.IntegrationTests.Ai;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Monitoring;

/// <summary>
/// P14.3 Periodic Reports (specification §35.3, owner decision D6): one subscription, several recipients, each report
/// computed under its recipient's own permissions and row scopes (never the owner's figures or narrative); a
/// recipient without permission gets the "no data" email; Management subscribes only themselves; three failed runs
/// pause the subscription; organizations and the Platform Administrator see nothing of each other.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class PeriodicReportsTests : IDisposable
{
    private const string OwnerNarrative = "Owner-only narrative: 42 critical tickets";

    private readonly SqlServerTestDatabase _db;
    private readonly AiApiFactory _factory;
    private readonly string _keyRing = Path.Combine(Path.GetTempPath(), "wi-keys-tests", "periodic-reports-tests");

    public PeriodicReportsTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new AiApiFactory(db.ConnectionString, new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = _keyRing, ["Ai:Anthropic:ApiKey"] = "sk-ant-seeded-platform-key-0004" });
    }

    public void Dispose() => _factory.Dispose();

    private const string TicketsByImpactPlan = """
        {"objective":"Tickets by impact","source":{"schema":"dbo","object":"SupportTickets","alias":"t"},"joins":[],
         "select":[{"alias":"impact","column":"t.Impact","aggregate":null,"dateTrunc":null},{"alias":"tickets","column":null,"aggregate":"count","dateTrunc":null}],
         "filters":null,"groupBy":["impact"],"orderBy":[{"alias":"tickets","direction":"desc"}],"limit":null,"visualization":{"type":"bar","x":"impact","y":["tickets"]},"assumptions":[]}
        """;

    [Fact]
    public async Task EachRecipientGetsTheirOwnFigures_TheUnpermittedGetNoData_ManagementSubscribesOnlyThemselves()
    {
        FakeAiProvider.Script = null;
        try
        {
            using var alice = await LoginAsync(TestData.AliceEmail);
            var (applicationId, objectId, columns) = await PrepareApplicationAsync(alice, "reports");
            foreach (var impact in new[] { "Low", "High", "High", "Critical" })
            {
                Assert.Equal(HttpStatusCode.Created, (await alice.PostJsonAsync("/api/support/tickets", new { ticketType = "Request", impact, subject = $"Report seed {impact}", description = "Seed ticket for periodic reports.", requiresSupportAccess = false })).StatusCode);
            }

            var roles = (await (await alice.GetAsync("/api/roles")).ReadJsonAsync()).EnumerateArray().ToList();
            Guid RoleId(string code) => roles.Single(r => r.GetProperty("roleCode").GetString() == code).GetProperty("roleId").GetGuid();
            var (managerId, managerEmail) = await InsertUserAsync(_db.Data.OrganizationAId, RoleId("Management"), "Report Manager");
            var (analystId, analystEmail) = await InsertUserAsync(_db.Data.OrganizationAId, RoleId("Analyst"), "Scoped Analyst");
            var (outsiderId, outsiderEmail) = await InsertUserAsync(_db.Data.OrganizationAId, null, "No Access User");

            // The analyst may read SupportTickets only where Impact equals their department code ("Critical").
            var impactColumn = columns.Single(c => c.GetProperty("columnName").GetString() == "Impact").GetProperty("columnId").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await alice.PutAsJsonAsync($"/api/permissions/roles/{RoleId("Analyst")}/objects/{objectId}", new { granted = true, allColumns = true, rowScope = "Department" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await alice.PutAsJsonAsync($"/api/permissions/objects/{objectId}/row-mapping", new { departmentColumnId = impactColumn })).StatusCode);
            var departmentId = await DepartmentAsync(alice, "Critical");
            Assert.Equal(HttpStatusCode.OK, (await alice.PutAsJsonAsync($"/api/users/{analystId}", new { fullName = "Scoped Analyst", departmentId, preferredLanguage = "en", roleCodes = new[] { "Analyst" } })).StatusCode);

            // The manager saves an analysis (with an AI narrative about their own figures) and shares it with the administrator.
            using var manager = await LoginAsync(managerEmail);
            FakeAiProvider.Script = request => request.SchemaName == "wabi_interpretation" ? $$"""{"narrative":"{{OwnerNarrative}}","highlights":["owner highlight"]}""" : TicketsByImpactPlan;
            var ask = await manager.PostJsonAsync("/api/ai/ask", new { applicationId, question = "Tickets by impact", language = "en" });
            Assert.True(ask.StatusCode == HttpStatusCode.OK, await ask.Content.ReadAsStringAsync());
            var historyId = (await ask.ReadJsonAsync()).GetProperty("historyId").GetGuid();
            FakeAiProvider.Script = null;
            Assert.Equal(HttpStatusCode.OK, (await manager.PostJsonAsync($"/api/history/{historyId}/shares", new { recipientUserIds = new[] { _db.Data.UserA1Id } })).StatusCode);

            // 1. The administrator schedules it for three people; the analyses offered are the administrator's own or shared ones.
            var analyses = (await (await alice.GetAsync("/api/subscriptions/analyses")).ReadJsonAsync()).EnumerateArray().ToList();
            var option = Assert.Single(analyses, a => a.GetProperty("historyId").GetGuid() == historyId);
            Assert.False(option.GetProperty("isOwn").GetBoolean());
            Assert.Equal("Report Manager", option.GetProperty("ownerName").GetString());
            var create = await alice.PostJsonAsync("/api/subscriptions", Body("Weekly tickets by impact", historyId, [managerId, analystId, outsiderId]));
            Assert.True(create.StatusCode == HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
            var subscription = await create.ReadJsonAsync();
            var subscriptionId = subscription.GetProperty("subscriptionId").GetGuid();
            Assert.True(subscription.TryGetProperty("nextRunAt", out _));
            Assert.Equal(3, subscription.GetProperty("recipients").GetArrayLength());

            // 2. Run now: two reports (different figures), one "no data".
            var run = await alice.PostAsync($"/api/subscriptions/{subscriptionId}/run-now", null);
            Assert.True(run.StatusCode == HttpStatusCode.OK, await run.Content.ReadAsStringAsync());
            var result = await run.ReadJsonAsync();
            Assert.Equal(2, result.GetProperty("delivered").GetInt32());
            Assert.Equal(1, result.GetProperty("noData").GetInt32());
            Assert.Equal(0, result.GetProperty("failed").GetInt32());
            var deliveries = result.GetProperty("deliveries").EnumerateArray().ToList();
            var managerRows = deliveries.Single(d => d.GetProperty("recipientUserId").GetGuid() == managerId).GetProperty("rowCount").GetInt32();
            var analystRows = deliveries.Single(d => d.GetProperty("recipientUserId").GetGuid() == analystId).GetProperty("rowCount").GetInt32();
            Assert.Equal(1, analystRows);                // only the Critical group
            Assert.True(managerRows >= 3, $"manager rows {managerRows}");
            Assert.Equal("NoData", deliveries.Single(d => d.GetProperty("recipientUserId").GetGuid() == outsiderId).GetProperty("status").GetString());

            var managerMail = await _factory.Emails.WaitForAsync(managerEmail, m => m.Subject.Contains("Weekly tickets by impact", StringComparison.Ordinal));
            var analystMail = await _factory.Emails.WaitForAsync(analystEmail, m => m.Subject.Contains("Weekly tickets by impact", StringComparison.Ordinal));
            var outsiderMail = await _factory.Emails.WaitForAsync(outsiderEmail, m => m.Subject.Contains("Weekly tickets by impact", StringComparison.Ordinal));
            var managerSheet = SheetText(Assert.Single(managerMail.Attachments));
            var analystSheet = SheetText(Assert.Single(analystMail.Attachments));
            Assert.Contains(">Low<", managerSheet, StringComparison.Ordinal);
            Assert.Contains(">Critical<", analystSheet, StringComparison.Ordinal);
            Assert.DoesNotContain(">Low<", analystSheet, StringComparison.Ordinal);
            Assert.DoesNotContain(">High<", analystSheet, StringComparison.Ordinal);
            // The owner's AI narrative describes the owner's figures; no recipient's file carries it.
            Assert.DoesNotContain(OwnerNarrative, managerSheet + analystSheet, StringComparison.Ordinal);
            Assert.Empty(outsiderMail.Attachments);
            Assert.Contains("No data is available for your permissions", outsiderMail.TextBody, StringComparison.Ordinal);
            Assert.Contains("/app/reports?tab=received", analystMail.TextBody, StringComparison.Ordinal);

            // 3. "Sent to me": each recipient sees and opens only their own delivery.
            using var analyst = await LoginAsync(analystEmail);
            var received = await (await analyst.GetAsync("/api/subscriptions/received")).ReadJsonAsync();
            var mine = Assert.Single(received.GetProperty("items").EnumerateArray());
            Assert.Equal("Weekly tickets by impact", mine.GetProperty("subscriptionName").GetString());
            var deliveryId = mine.GetProperty("deliveryId").GetGuid();
            var file = await analyst.GetAsync($"/api/subscriptions/received/{deliveryId}/file");
            Assert.Equal(HttpStatusCode.OK, file.StatusCode);
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.Content.Headers.ContentType?.MediaType);
            Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/subscriptions/received/{deliveryId}/file")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/subscriptions/received/{deliveryId}/file")).StatusCode);
            var log = await (await alice.GetAsync($"/api/subscriptions/{subscriptionId}/deliveries")).ReadJsonAsync();
            Assert.Equal(3, log.GetProperty("totalCount").GetInt32());

            // 4. D6: Management subscribes only themselves; analysts manage nothing.
            Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostJsonAsync("/api/subscriptions", Body("For someone else", historyId, [analystId]))).StatusCode);
            var own = await manager.PostJsonAsync("/api/subscriptions", Body("My own weekly tickets", historyId, []));
            Assert.Equal(HttpStatusCode.Created, own.StatusCode);
            var ownRecipient = Assert.Single((await own.ReadJsonAsync()).GetProperty("recipients").EnumerateArray());
            Assert.Equal(managerId, ownRecipient.GetProperty("userId").GetGuid());
            Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/subscriptions/{subscriptionId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await analyst.GetAsync("/api/subscriptions")).StatusCode);
            // An analysis that is neither the author's nor shared with them cannot be scheduled.
            var refused = await analyst.PostJsonAsync("/api/subscriptions", Body("Not mine", historyId, []));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

            // 5. Organization B and the Platform Administrator see nothing.
            var (_, bAdminEmail) = await InsertUserAsync(_db.Data.OrganizationBId, _db.Data.RoleAdminBId, "B Admin");
            using var bAdmin = await LoginAsync(bAdminEmail);
            Assert.Equal(HttpStatusCode.NotFound, (await bAdmin.GetAsync($"/api/subscriptions/{subscriptionId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await bAdmin.GetAsync($"/api/subscriptions/received/{deliveryId}/file")).StatusCode);
            Assert.DoesNotContain((await (await bAdmin.GetAsync("/api/subscriptions")).ReadJsonAsync()).EnumerateArray(), s => s.GetProperty("subscriptionId").GetGuid() == subscriptionId);
            using var platform = await LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
            Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync("/api/subscriptions")).StatusCode);

            // Append-only log.
            await using var context = _db.CreateContext(TestData.SystemPrincipal);
            var delivery = await context.ReportDeliveries.IgnoreQueryFilters().FirstAsync(d => d.SubscriptionId == subscriptionId);
            delivery.RowCount = 99;
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
        finally
        {
            FakeAiProvider.Script = null;
        }
    }

    [Fact]
    public async Task ThreeFailedScheduledRuns_PauseTheSubscription_AndNotifyItsCreator()
    {
        FakeAiProvider.Script = null;
        try
        {
            using var alice = await LoginAsync(TestData.AliceEmail);
            var (applicationId, _, _) = await PrepareApplicationAsync(alice, "reports-pause");
            var roles = (await (await alice.GetAsync("/api/roles")).ReadJsonAsync()).EnumerateArray().ToList();
            var managementId = roles.Single(r => r.GetProperty("roleCode").GetString() == "Management").GetProperty("roleId").GetGuid();
            var (managerId, managerEmail) = await InsertUserAsync(_db.Data.OrganizationAId, managementId, "Pausing Manager");
            using var manager = await LoginAsync(managerEmail);
            FakeAiProvider.Script = request => request.SchemaName == "wabi_interpretation" ? """{"narrative":"n","highlights":[]}""" : TicketsByImpactPlan;
            var historyId = (await (await manager.PostJsonAsync("/api/ai/ask", new { applicationId, question = "Tickets by impact", language = "en" })).ReadJsonAsync()).GetProperty("historyId").GetGuid();
            FakeAiProvider.Script = null;
            var created = await (await manager.PostJsonAsync("/api/subscriptions", Body("Daily impact for me", historyId, [], "Daily"))).ReadJsonAsync();
            var subscriptionId = created.GetProperty("subscriptionId").GetGuid();

            // The analysis is deleted: every scheduled run fails; the third pauses the subscription.
            Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync(new Uri($"/api/history/{historyId}", UriKind.Relative))).StatusCode);
            for (var i = 0; i < 3; i++)
            {
                await RunScheduledAsync(subscriptionId);
            }

            var paused = await (await manager.GetAsync($"/api/subscriptions/{subscriptionId}")).ReadJsonAsync();
            Assert.False(paused.GetProperty("isActive").GetBoolean());
            Assert.Equal("subscription_analysis_missing", paused.GetProperty("pausedReason").GetString());
            Assert.Equal(3, paused.GetProperty("consecutiveFailures").GetInt32());
            Assert.False(paused.TryGetProperty("nextRunAt", out _));
            var notifications = (await (await manager.GetAsync("/api/notifications?unreadOnly=true")).ReadJsonAsync()).GetProperty("items").EnumerateArray();
            Assert.Contains(notifications, n => n.GetProperty("notificationType").GetString() == "Reports.SubscriptionPaused" && n.GetProperty("entityId").GetString() == subscriptionId.ToString());
            // Resuming needs an analysis the author can still schedule.
            var resume = await manager.PostAsync($"/api/subscriptions/{subscriptionId}/resume", null);
            Assert.Equal(HttpStatusCode.BadRequest, resume.StatusCode);
            Assert.Equal("subscription_analysis_not_available", (await resume.ReadJsonAsync()).GetProperty("detail").GetString());
            _ = managerId;
        }
        finally
        {
            FakeAiProvider.Script = null;
        }
    }

    private static object Body(string name, Guid historyId, Guid[] recipients, string recurrence = "Weekly") => new
    {
        name,
        historyId,
        recurrenceKind = recurrence,
        timeOfDay = "07:00:00",
        dayOfWeek = recurrence == "Weekly" ? 1 : (int?)null,
        dayOfMonth = (int?)null,
        format = "Xlsx",
        language = "recipient",
        isActive = true,
        recipientUserIds = recipients,
    };

    private static string SheetText(EmailAttachment attachment)
    {
        using var zip = new ZipArchive(new MemoryStream(attachment.Content), ZipArchiveMode.Read);
        var text = new StringBuilder();
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(entry.Open());
            text.Append(reader.ReadToEnd());
        }

        return text.ToString();
    }

    private async Task RunScheduledAsync(Guid subscriptionId)
    {
        await using (var context = _db.CreateContext(TestData.SystemPrincipal))
        {
            var job = await context.ScheduledJobs.SingleAsync(j => j.JobType == ScheduledJobType.ReportSubscription && j.TargetId == subscriptionId);
            job.NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await context.SaveChangesAsync();
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextInitializer>().Initialize(ScheduledJobRunner.SchedulerPrincipal);
        Assert.True(await scope.ServiceProvider.GetRequiredService<ScheduledJobRunner>().RunDueJobsAsync("reports-tests") >= 1);
    }

    private static async Task<Guid> DepartmentAsync(HttpClient alice, string code)
    {
        // Other tests in the collection may already have created the department (codes compare without case).
        var created = await alice.PostJsonAsync("/api/departments", new { departmentCode = code, departmentNameEn = code, departmentNameAr = code });
        if (created.IsSuccessStatusCode)
        {
            return (await created.ReadJsonAsync()).GetProperty("departmentId").GetGuid();
        }

        var all = (await (await alice.GetAsync("/api/departments?includeInactive=true")).ReadJsonAsync()).EnumerateArray().ToList();
        var existing = all.FirstOrDefault(d => string.Equals(d.GetProperty("departmentCode").GetString(), code, StringComparison.OrdinalIgnoreCase));
        Assert.True(existing.ValueKind == JsonValueKind.Object, $"department {code}: {(int)created.StatusCode} {await created.Content.ReadAsStringAsync()}");
        return existing.GetProperty("departmentId").GetGuid();
    }

    private async Task<HttpClient> LoginAsync(string email, string password = TestData.UserPassword)
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, password)).StatusCode);
        return client;
    }

    private async Task<(Guid ApplicationId, Guid ObjectId, List<JsonElement> Columns)> PrepareApplicationAsync(HttpClient alice, string tag)
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
        return (applicationId, objectId, approved.GetProperty("columns").EnumerateArray().ToList());
    }

    private async Task<(Guid UserId, string Email)> InsertUserAsync(Guid organizationId, Guid? roleId, string fullName)
    {
        var email = $"reports.{Guid.NewGuid():N}@org.test";
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
