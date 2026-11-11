using System.Net;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Entities.Analysis;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Ai;

/// <summary>Sharing (specification §24, review B11): in-app VIEW shares with expiry and revocation, recipient read-only access, notifications, and classified email delivery.</summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class SharingTests : IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly AiApiFactory _factory;
    private readonly string _keyRing = Path.Combine(Path.GetTempPath(), "wi-keys-tests", "ai-sharing-tests");

    public SharingTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new AiApiFactory(db.ConnectionString, new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = _keyRing, ["Ai:Anthropic:ApiKey"] = "sk-ant-seeded-platform-key-0004", ["Platform:BaseUrl"] = "https://insight.example.test" });
    }

    public void Dispose() => _factory.Dispose();

    private async Task<HttpClient> LoginAsync(string email, string password = TestData.UserPassword)
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, password)).StatusCode);
        return client;
    }

    private const string StudyPlan = """
        {"objective":"Ticket study","assumptions":[],"deliverables":["report","email"],"queries":[
          {"title":"Total tickets","source":{"schema":"dbo","object":"SupportTickets","alias":"t"},"joins":[],"select":[{"alias":"tickets","column":null,"aggregate":"count","dateTrunc":null}],"filters":null,"groupBy":[],"orderBy":[],"limit":null,"visualization":{"type":"kpi","x":null,"y":["tickets"]}},
          {"title":"Tickets by status","source":{"schema":"dbo","object":"SupportTickets","alias":"t"},"joins":[],"select":[{"alias":"status","column":"t.Status","aggregate":null,"dateTrunc":null},{"alias":"tickets","column":null,"aggregate":"count","dateTrunc":null}],"filters":null,"groupBy":["status"],"orderBy":[{"alias":"tickets","direction":"desc"}],"limit":null,"visualization":{"type":"bar","x":"status","y":["tickets"]}}]}
        """;

    private const string StudyReport = """
        {"title":"Ticket study","subtitle":"All tickets","introduction":"Purpose.","sections":[{"title":"Overview","body":"Numbers.","panels":[0]},{"title":"By status","body":"Breakdown.","panels":[1]}],
         "summary":"Most tickets are open.","keyFindings":["Finding one"],"recommendations":[{"title":"Triage daily","detail":"Add a triage system."}],
         "email":{"subject":"Ticket study attached","body":"Dear director,\n\nPlease find the study attached.\n\nRegards"}}
        """;

    [Fact]
    public async Task Sharing_InApp_IsViewOnly_Scoped_Revocable_AndEmailFollowsClassificationRules()
    {
        FakeAiProvider.Script = null;
        try
        {
            using var alice = await LoginAsync(TestData.AliceEmail);
            var source = new SqlConnectionStringBuilder(_db.ConnectionString) { WorkstationID = "aisharing" }.ConnectionString;
            var created = await (await alice.PostJsonAsync("/api/applications", new { applicationCode = "aisharing", applicationName = "Sharing app", applicationNameAr = "تطبيق المشاركة", databaseType = "SqlServer", connectionString = source })).ReadJsonAsync();
            var applicationId = created.GetProperty("applicationId").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/applications/{applicationId}/test-connection", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/applications/{applicationId}/approval", new { isApproved = true })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/metadata/applications/{applicationId}/refresh", new { })).StatusCode);
            var objects = await (await alice.GetAsync($"/api/metadata/applications/{applicationId}/objects?search=SupportTickets&pageSize=10")).ReadJsonAsync();
            var objectId = objects.GetProperty("items").EnumerateArray().Single(o => o.GetProperty("schemaName").GetString() == "dbo" && o.GetProperty("objectName").GetString() == "SupportTickets").GetProperty("objectId").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/metadata/objects/{objectId}/approve", new { approve = true })).StatusCode);
            var roles = (await (await alice.GetAsync("/api/roles")).ReadJsonAsync()).EnumerateArray().ToList();
            var managementId = roles.Single(r => r.GetProperty("roleCode").GetString() == "Management").GetProperty("roleId").GetGuid();
            var (ownerId, ownerEmail) = await InsertUserAsync(_db.Data.OrganizationAId, managementId, "Share Owner");
            var (colleagueId, colleagueEmail) = await InsertUserAsync(_db.Data.OrganizationAId, managementId, "Share Colleague");
            var (_, outsiderEmail) = await InsertUserAsync(_db.Data.OrganizationAId, managementId, "Share Outsider");
            var (inactiveId, _) = await InsertUserAsync(_db.Data.OrganizationAId, managementId, "Share Inactive", OrganizationUserStatus.Inactive);
            using var owner = await LoginAsync(ownerEmail);
            using var colleague = await LoginAsync(colleagueEmail);
            using var outsider = await LoginAsync(outsiderEmail);
            using var bob = await LoginAsync(TestData.BobEmail);

            FakeAiProvider.Script = (request) => request.SchemaName == "wabi_report" ? StudyReport : StudyPlan;
            var ask = await owner.PostJsonAsync("/api/ai/ask", new { applicationId, question = "Write a ticket study as PDF and an email", language = "en" });
            Assert.True(ask.StatusCode == HttpStatusCode.OK, await ask.Content.ReadAsStringAsync());
            var answer = await ask.ReadJsonAsync();
            var historyId = answer.GetProperty("historyId").GetGuid();
            Assert.NotNull(answer.GetProperty("emailDraft").GetProperty("subject").GetString());

            // Recipients: active colleagues of the same organization, never the caller, never disabled accounts.
            var recipients = (await (await owner.GetAsync("/api/shares/recipients?search=Share")).ReadJsonAsync()).EnumerateArray().Select(r => r.GetProperty("userId").GetGuid()).ToList();
            Assert.Contains(colleagueId, recipients);
            Assert.DoesNotContain(ownerId, recipients);
            Assert.DoesNotContain(inactiveId, recipients);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostJsonAsync($"/api/history/{historyId}/shares", new { recipientUserIds = new[] { inactiveId } })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostJsonAsync($"/api/history/{historyId}/shares", new { recipientUserIds = new[] { _db.Data.UserB1Id } })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await colleague.PostJsonAsync($"/api/history/{historyId}/shares", new { recipientUserIds = new[] { colleagueId } })).StatusCode);

            // Share with a message and an expiry: the colleague is notified in-app and by email.
            var expires = DateTimeOffset.UtcNow.AddDays(7);
            var shared = await owner.PostJsonAsync($"/api/history/{historyId}/shares", new { recipientUserIds = new[] { colleagueId }, message = "Have a look before Monday.", expiresAt = expires });
            Assert.True(shared.StatusCode == HttpStatusCode.OK, await shared.Content.ReadAsStringAsync());
            var share = Assert.Single((await shared.ReadJsonAsync()).EnumerateArray());
            var shareId = share.GetProperty("shareId").GetGuid();
            Assert.Equal("Active", share.GetProperty("status").GetString());
            Assert.Equal("View", share.GetProperty("permission").GetString());
            Assert.Equal("Share Colleague", share.GetProperty("recipientName").GetString());
            var notification = Assert.Single((await (await colleague.GetAsync("/api/notifications?unreadOnly=true")).ReadJsonAsync()).GetProperty("items").EnumerateArray(), n => n.GetProperty("notificationType").GetString() == "Analysis.Shared");
            Assert.Equal(historyId.ToString(), notification.GetProperty("entityId").GetString());
            var shareEmail = await _factory.Emails.WaitForAsync(colleagueEmail);
            Assert.Contains("Ticket study", shareEmail.Subject, StringComparison.Ordinal);
            Assert.Contains($"/app/shared-with-me/{historyId}", shareEmail.TextBody, StringComparison.Ordinal);

            // Sharing again with the same colleague refreshes the terms; no duplicate row.
            Assert.Equal(HttpStatusCode.OK, (await owner.PostJsonAsync($"/api/history/{historyId}/shares", new { recipientUserIds = new[] { colleagueId }, message = "Updated note" })).StatusCode);
            var sharesOfResponse = await owner.GetAsync($"/api/history/{historyId}/shares");
            Assert.True(sharesOfResponse.StatusCode == HttpStatusCode.OK, await sharesOfResponse.Content.ReadAsStringAsync());
            var sharesOf = (await sharesOfResponse.ReadJsonAsync()).EnumerateArray().ToList();
            Assert.Single(sharesOf);
            Assert.Equal("Updated note", sharesOf[0].GetProperty("message").GetString());
            Assert.False(sharesOf[0].TryGetProperty("expiresAt", out var refreshedExpiry) && refreshedExpiry.ValueKind != System.Text.Json.JsonValueKind.Null);

            // Recipient: Shared With Me lists it; detail, stored result and PDF open; nothing else does.
            var withMe = await (await colleague.GetAsync("/api/shares/with-me")).ReadJsonAsync();
            var row = Assert.Single(withMe.GetProperty("items").EnumerateArray());
            Assert.Equal(historyId, row.GetProperty("historyId").GetGuid());
            Assert.Equal("Share Owner", row.GetProperty("ownerName").GetString());
            Assert.True(row.GetProperty("hasReport").GetBoolean());
            var detail = await (await colleague.GetAsync($"/api/shares/with-me/{historyId}")).ReadJsonAsync();
            Assert.Equal("Write a ticket study as PDF and an email", detail.GetProperty("originalPrompt").GetString());
            Assert.Single(detail.GetProperty("versions").EnumerateArray());
            Assert.False(detail.GetProperty("versions")[0].TryGetProperty("estimatedCost", out _));
            Assert.False(detail.GetProperty("versions")[0].TryGetProperty("modelName", out _));
            var requestsBefore = FakeAiProvider.Requests.Count;
            var stored = await (await colleague.GetAsync($"/api/shares/with-me/{historyId}/versions/1/result")).ReadJsonAsync();
            Assert.Equal(answer.GetProperty("panels")[1].GetProperty("result").GetProperty("rows").GetRawText(), stored.GetProperty("panels")[1].GetProperty("result").GetProperty("rows").GetRawText());
            Assert.Equal(requestsBefore, FakeAiProvider.Requests.Count);
            var pdf = await colleague.GetAsync($"/api/shares/with-me/{historyId}/versions/1/report");
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
            Assert.Equal(HttpStatusCode.NotFound, (await colleague.GetAsync($"/api/history/{historyId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await colleague.PostJsonAsync($"/api/history/{historyId}/rerun", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await colleague.PostJsonAsync($"/api/history/{historyId}/versions/1/export/Pdf", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await colleague.DeleteAsync(new Uri($"/api/history/{historyId}", UriKind.Relative))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await colleague.DeleteAsync(new Uri($"/api/history/{historyId}/shares/{shareId}", UriKind.Relative))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/shares/with-me/{historyId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/shares/with-me/{historyId}/versions/1/result")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/shares/with-me/{historyId}")).StatusCode);
            Assert.Empty((await (await bob.GetAsync("/api/shares/with-me")).ReadJsonAsync()).GetProperty("items").EnumerateArray());

            // Shared By Me for the owner; last viewed is recorded.
            var byMe = await (await owner.GetAsync("/api/shares/by-me")).ReadJsonAsync();
            var mine = Assert.Single(byMe.GetProperty("items").EnumerateArray());
            Assert.Equal(colleagueId, mine.GetProperty("recipientUserId").GetGuid());
            Assert.True(mine.TryGetProperty("lastViewedAt", out var lastViewed) && lastViewed.ValueKind == System.Text.Json.JsonValueKind.String);

            // An expired share is invisible to the recipient and reported as Expired to the owner.
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostJsonAsync($"/api/history/{historyId}/shares", new { recipientUserIds = new[] { colleagueId }, expiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) })).StatusCode);
            await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
            {
                await context.AnalysisShares.IgnoreQueryFilters().Where(s => s.ShareId == shareId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
            }

            Assert.Equal(HttpStatusCode.NotFound, (await colleague.GetAsync($"/api/shares/with-me/{historyId}")).StatusCode);
            Assert.Empty((await (await colleague.GetAsync("/api/shares/with-me")).ReadJsonAsync()).GetProperty("items").EnumerateArray());
            Assert.Empty((await (await owner.GetAsync("/api/shares/by-me")).ReadJsonAsync()).GetProperty("items").EnumerateArray());
            Assert.Equal("Expired", Assert.Single((await (await owner.GetAsync("/api/shares/by-me?includeInactive=true")).ReadJsonAsync()).GetProperty("items").EnumerateArray()).GetProperty("status").GetString());

            // Sharing again revives it (same row, new terms); revoking ends it and notifies the colleague.
            Assert.Equal(HttpStatusCode.OK, (await owner.PostJsonAsync($"/api/history/{historyId}/shares", new { recipientUserIds = new[] { colleagueId } })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await colleague.GetAsync($"/api/shares/with-me/{historyId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(new Uri($"/api/history/{historyId}/shares/{shareId}", UriKind.Relative))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await colleague.GetAsync($"/api/shares/with-me/{historyId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await colleague.GetAsync($"/api/shares/with-me/{historyId}/versions/1/report")).StatusCode);
            Assert.Equal("Revoked", Assert.Single((await (await owner.GetAsync($"/api/history/{historyId}/shares")).ReadJsonAsync()).EnumerateArray()).GetProperty("status").GetString());
            Assert.Contains((await (await colleague.GetAsync("/api/notifications")).ReadJsonAsync()).GetProperty("items").EnumerateArray(), n => n.GetProperty("notificationType").GetString() == "Analysis.ShareRevoked");

            // Email delivery. Internal data: owner's text, the study PDF attached, colleague gets a link and an in-app share, the external address gets the file only.
            Assert.Equal("share_email_recipient_invalid", (await (await owner.PostJsonAsync($"/api/history/{historyId}/email", new { versionNumber = 1, recipients = new[] { "not an address" }, subject = "S", body = "B" })).ReadJsonAsync()).GetProperty("detail").GetString());
            var sent = await owner.PostJsonAsync($"/api/history/{historyId}/email", new { versionNumber = 1, recipients = new[] { colleagueEmail, "director@partner.example" }, subject = "Ticket study attached", body = "Dear director,\n\nPlease find the study attached.", attachReport = true });
            Assert.True(sent.StatusCode == HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
            var result = await sent.ReadJsonAsync();
            Assert.Equal(2, result.GetProperty("recipients").GetInt32());
            Assert.Equal(1, result.GetProperty("internalRecipients").GetInt32());
            Assert.Equal(1, result.GetProperty("externalRecipients").GetInt32());
            Assert.True(result.GetProperty("attachedReport").GetBoolean());
            Assert.Equal(1, result.GetProperty("inAppSharesCreated").GetInt32());
            var external = await _factory.Emails.WaitForAsync("director@partner.example");
            Assert.Equal("Ticket study attached", external.Subject);
            var attachment = Assert.Single(external.Attachments);
            Assert.EndsWith(".pdf", attachment.FileName, StringComparison.Ordinal);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(attachment.Content, 0, 4));
            Assert.DoesNotContain("/app/shared-with-me/", external.TextBody, StringComparison.Ordinal);
            Assert.Contains("Please find the study attached.", external.TextBody, StringComparison.Ordinal);
            var internalMail = _factory.Emails.Messages.Single(m => m.ToEmail == colleagueEmail && m.Subject == "Ticket study attached");
            Assert.Contains($"/app/shared-with-me/{historyId}", internalMail.TextBody, StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.OK, (await colleague.GetAsync($"/api/shares/with-me/{historyId}")).StatusCode);

            // Confidential: the owner's explicit disclosure approval is required and logged. Restricted: never to an external address.
            await SetClassificationAsync(historyId, DataClassification.Confidential);
            var refused = await owner.PostJsonAsync($"/api/history/{historyId}/email", new { versionNumber = 1, recipients = new[] { "director@partner.example" }, subject = "S", body = "B", attachReport = false, disclosureApproved = false });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("share_disclosure_approval_required", (await refused.ReadJsonAsync()).GetProperty("detail").GetString());
            Assert.Equal(HttpStatusCode.OK, (await owner.PostJsonAsync($"/api/history/{historyId}/email", new { versionNumber = 1, recipients = new[] { "director@partner.example" }, subject = "Approved", body = "Approved disclosure.", attachReport = false, disclosureApproved = true })).StatusCode);
            Assert.Empty((await _factory.Emails.WaitForAsync("director@partner.example", m => m.Subject == "Approved")).Attachments);
            await SetClassificationAsync(historyId, DataClassification.Restricted);
            var blocked = await owner.PostJsonAsync($"/api/history/{historyId}/email", new { versionNumber = 1, recipients = new[] { colleagueEmail, "director@partner.example" }, subject = "S", body = "B", disclosureApproved = true });
            Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
            Assert.Equal("share_restricted_external_blocked", (await blocked.ReadJsonAsync()).GetProperty("detail").GetString());
            Assert.Equal(HttpStatusCode.OK, (await owner.PostJsonAsync($"/api/history/{historyId}/email", new { versionNumber = 1, recipients = new[] { colleagueEmail }, subject = "Internal only", body = "Stays inside.", attachReport = false })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await colleague.PostJsonAsync($"/api/history/{historyId}/email", new { versionNumber = 1, recipients = new[] { "x@y.example" }, subject = "S", body = "B" })).StatusCode);

            await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
            {
                var events = await context.EmailDraftEvents.IgnoreQueryFilters().Where(e => e.HistoryId == historyId).OrderBy(e => e.CreatedAt).ToListAsync();
                Assert.Equal([EmailDraftEventKind.Drafted, EmailDraftEventKind.Sent, EmailDraftEventKind.Blocked, EmailDraftEventKind.Sent, EmailDraftEventKind.Blocked, EmailDraftEventKind.Sent], events.Select(e => e.Kind));
                Assert.Equal(1, events[1].InternalRecipientCount);
                Assert.Equal(1, events[1].ExternalRecipientCount);
                Assert.True(events[1].AttachedReport);
                Assert.Equal("share_disclosure_approval_required", events[2].BlockReason);
                Assert.True(events[3].DisclosureApproved);
                Assert.Equal("share_restricted_external_blocked", events[4].BlockReason);
                Assert.Equal(DataClassification.Restricted, events[5].DataClassification);
                Assert.Equal(0, events[5].ExternalRecipientCount);
                Assert.Equal(3, await context.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.EntityId == historyId.ToString() && a.EventType == "Share.EmailSent"));
                Assert.Equal(2, await context.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.EntityId == historyId.ToString() && a.EventType == "Share.EmailBlocked"));
                Assert.Equal(3, await context.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.EntityId == historyId.ToString() && a.EventType == "Share.Created"));
                Assert.Equal(1, await context.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.EntityId == historyId.ToString() && a.EventType == "Share.Revoked"));
                events[0].Recipients = "tampered";
                await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            }
        }
        finally
        {
            FakeAiProvider.Script = null;
        }
    }

    private async Task SetClassificationAsync(Guid historyId, DataClassification classification)
    {
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        await context.AnalysisHistories.IgnoreQueryFilters().Where(h => h.HistoryId == historyId).ExecuteUpdateAsync(s => s.SetProperty(h => h.DataClassification, classification));
    }

    private async Task<(Guid UserId, string Email)> InsertUserAsync(Guid organizationId, Guid roleId, string fullName, OrganizationUserStatus status = OrganizationUserStatus.Active)
    {
        var email = $"sharing.{Guid.NewGuid():N}@org.test";
        var userId = Core.Common.Ids.New();
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var now = DateTimeOffset.UtcNow;
        context.OrganizationUsers.Add(TestData.NewUser(userId, organizationId, email, fullName, status));
        context.LoginIdentities.Add(TestData.Identity(email, Core.Tenancy.PrincipalType.OrganizationUser, userId, organizationId, now));
        context.UserRoles.Add(new UserRole { OrganizationId = organizationId, UserId = userId, RoleId = roleId, AssignedAt = now, AssignedByAdministratorId = _db.Data.AdministratorId });
        await context.SaveChangesAsync();
        return (userId, email);
    }
}
