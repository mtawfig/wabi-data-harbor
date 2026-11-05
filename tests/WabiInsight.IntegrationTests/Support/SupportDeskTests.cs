using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Entities.Support;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Support;

[Collection(DatabaseCollectionDefinition.Name)]
public sealed class SupportDeskTests : IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly ApiFactory _factory;
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "wi-support-tests", Guid.NewGuid().ToString("N"));

    public SupportDeskTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new ApiFactory(db.ConnectionString, new Dictionary<string, string?> { ["Storage:RootPath"] = _storageRoot });
    }

    public void Dispose()
    {
        _factory.Dispose();
        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    private async Task<HttpClient> LoginAsync(string email, string password = TestData.UserPassword)
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, password)).StatusCode);
        return client;
    }

    /// <summary>Creates a fresh platform administrator through the API (other tests reset the seeded second administrator's password).</summary>
    private async Task<(HttpClient Client, Guid AdministratorId)> NewPlatformAdministratorAsync(HttpClient platform, string email)
    {
        var create = await platform.PostJsonAsync("/api/platform/administrators", new { email, fullName = "Desk Admin" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var administratorId = (await create.ReadJsonAsync()).GetProperty("id").GetGuid();
        var temporary = Email.RecordingEmailSender.TemporaryPasswordOf(await _factory.Emails.WaitForAsync(email));
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, temporary)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostJsonAsync("/api/auth/change-password", new { currentPassword = temporary, newPassword = "Desk#Admin#Strong#2026" })).StatusCode);
        await client.RefreshCsrfAsync();
        return (client, administratorId);
    }

    private static async Task<JsonElement> CreateTicketAsync(HttpClient client, string subject, string type = "Request", string impact = "Medium")
    {
        var response = await client.PostJsonAsync("/api/support/tickets", new { ticketType = type, impact, category = "Access", subject, description = $"Description for {subject}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static Guid IdOf(JsonElement ticket) => ticket.GetProperty("summary").GetProperty("ticketId").GetGuid();

    private static string StatusOf(JsonElement ticket) => ticket.GetProperty("summary").GetProperty("status").GetString()!;

    [Fact]
    public async Task Requester_CreatesTicket_WithSequentialReference_TimelineAndPlatformNotification()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        var first = await CreateTicketAsync(alice, "Cannot open dashboard", "Incident", "High");
        var second = await CreateTicketAsync(alice, "Request new report");

        var year = DateTimeOffset.UtcNow.Year;
        var firstReference = first.GetProperty("summary").GetProperty("referenceNumber").GetString()!;
        var secondReference = second.GetProperty("summary").GetProperty("referenceNumber").GetString()!;
        Assert.Matches($"^WI-{year}-[0-9]{{6}}$", firstReference);
        Assert.Equal(long.Parse(firstReference[^6..], System.Globalization.CultureInfo.InvariantCulture) + 1, long.Parse(secondReference[^6..], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("New", StatusOf(first));
        Assert.Equal(_db.Data.OrganizationAId, first.GetProperty("summary").GetProperty("organizationId").GetGuid());
        Assert.Equal("Alice A", first.GetProperty("summary").GetProperty("createdByName").GetString());
        Assert.Contains("Cancelled", first.GetProperty("allowedTransitions").EnumerateArray().Select(s => s.GetString()));
        Assert.DoesNotContain("Started", first.GetProperty("allowedTransitions").EnumerateArray().Select(s => s.GetString()));

        var events = await (await alice.GetAsync($"/api/support/tickets/{IdOf(first)}/events")).ReadJsonAsync();
        var created = Assert.Single(events.EnumerateArray());
        Assert.Equal("TicketCreated", created.GetProperty("eventType").GetString());
        Assert.Equal("New", created.GetProperty("newStatus").GetString());
        Assert.Equal("Alice A", created.GetProperty("actorDisplayName").GetString());

        // Every active platform administrator is notified in-app and by email.
        using var platform = await LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        var unread = await (await platform.GetAsync("/api/notifications/unread-count")).ReadJsonAsync();
        Assert.True(unread.GetProperty("count").GetInt32() >= 2);
        var notifications = await (await platform.GetAsync("/api/notifications?unreadOnly=true")).ReadJsonAsync();
        var notification = notifications.GetProperty("items").EnumerateArray().First(n => n.GetProperty("entityId").GetString() == IdOf(first).ToString());
        Assert.Equal("Ticket.Created", notification.GetProperty("notificationType").GetString());
        Assert.Contains(firstReference, notification.GetProperty("dataJson").GetString(), StringComparison.Ordinal);
        var email = await _factory.Emails.WaitForAsync(TestData.AdminEmail);
        Assert.Contains(firstReference, email.Subject, StringComparison.Ordinal);
        Assert.Contains("/platform/support/", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Visibility_RequesterOwnOnly_OrgAdminWholeOrganization_NeverAcrossOrganizations()
    {
        // Carol-like second user in organization A without the admin role.
        using var alice = await LoginAsync(TestData.AliceEmail); // OrganizationAdmin of A
        var create = await alice.PostJsonAsync("/api/users", new { email = "requester2@org-a.test", fullName = "Second Requester", preferredLanguage = "en", roleCodes = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var temporary = Email.RecordingEmailSender.TemporaryPasswordOf(await _factory.Emails.WaitForAsync("requester2@org-a.test"));
        using var requester = _factory.CreateClient();
        await requester.LoginAsync("requester2@org-a.test", temporary);
        await requester.PostJsonAsync("/api/auth/change-password", new { currentPassword = temporary, newPassword = "Requester#Strong#2026" });
        await requester.RefreshCsrfAsync();

        var aliceTicket = await CreateTicketAsync(alice, "Alice ticket");
        var requesterTicket = await CreateTicketAsync(requester, "Requester ticket");

        // Requester: own only.
        var mine = await (await requester.GetAsync("/api/support/tickets")).ReadJsonAsync();
        Assert.All(mine.GetProperty("items").EnumerateArray(), t => Assert.Equal("Second Requester", t.GetProperty("createdByName").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await requester.GetAsync($"/api/support/tickets/{IdOf(aliceTicket)}")).StatusCode);

        // OrganizationAdmin: whole organization.
        var organization = await (await alice.GetAsync("/api/support/tickets?pageSize=100")).ReadJsonAsync();
        var ids = organization.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("ticketId").GetGuid()).ToList();
        Assert.Contains(IdOf(aliceTicket), ids);
        Assert.Contains(IdOf(requesterTicket), ids);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/support/tickets/{IdOf(requesterTicket)}")).StatusCode);

        // Another organization: nothing, not even by id.
        using var bob = await LoginAsync(TestData.BobEmail);
        var other = await (await bob.GetAsync("/api/support/tickets?pageSize=100")).ReadJsonAsync();
        Assert.DoesNotContain(other.GetProperty("items").EnumerateArray(), t => ids.Contains(t.GetProperty("ticketId").GetGuid()));
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/support/tickets/{IdOf(aliceTicket)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/support/tickets/{IdOf(aliceTicket)}/comments")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostJsonAsync($"/api/support/tickets/{IdOf(aliceTicket)}/comments", new { body = "intruder" })).StatusCode);

        // Organization users cannot use the platform queue; platform administrators see both organizations.
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/platform/support/tickets")).StatusCode);
        using var platform = await LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        var queue = await (await platform.GetAsync("/api/platform/support/tickets?pageSize=200")).ReadJsonAsync();
        var queueIds = queue.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("ticketId").GetGuid()).ToList();
        Assert.Contains(IdOf(aliceTicket), queueIds);
        Assert.Contains(IdOf(requesterTicket), queueIds);
        var filtered = await (await platform.GetAsync($"/api/platform/support/tickets?organizationId={_db.Data.OrganizationBId}&pageSize=200")).ReadJsonAsync();
        Assert.All(filtered.GetProperty("items").EnumerateArray(), t => Assert.Equal(_db.Data.OrganizationBId, t.GetProperty("organizationId").GetGuid()));
    }

    [Fact]
    public async Task Lifecycle_FirstOpen_Assign_Pending_AutoReply_Resolve_Reopen_WithTimeline()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        var ticket = await CreateTicketAsync(alice, "Lifecycle ticket", "Incident", "Critical");
        var id = IdOf(ticket);

        using var platform = await LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        var opened = await (await platform.GetAsync($"/api/platform/support/tickets/{id}")).ReadJsonAsync();
        Assert.Equal("Read", StatusOf(opened));
        Assert.Equal(_db.Data.AdministratorId, opened.GetProperty("firstOpenedByAdministratorId").GetGuid());
        var firstOpenedAt = opened.GetProperty("firstOpenedAt").GetDateTimeOffset();

        // A second administrator's view never overwrites the first-open fields and adds no timeline event.
        var (second, secondId) = await NewPlatformAdministratorAsync(platform, "desk.second@wabitex.test");
        using var _ = second;
        var again = await (await second.GetAsync($"/api/platform/support/tickets/{id}")).ReadJsonAsync();
        Assert.Equal(_db.Data.AdministratorId, again.GetProperty("firstOpenedByAdministratorId").GetGuid());
        Assert.Equal(firstOpenedAt, again.GetProperty("firstOpenedAt").GetDateTimeOffset());
        var afterViews = await (await platform.GetAsync($"/api/platform/support/tickets/{id}/events")).ReadJsonAsync();
        Assert.Equal(1, afterViews.EnumerateArray().Count(e => e.GetProperty("eventType").GetString() == "TicketViewed"));

        // Assignment notifies the assignee.
        var assign = await platform.PostJsonAsync($"/api/platform/support/tickets/{id}/assign", new { administratorId = secondId });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        Assert.Equal("Desk Admin", (await assign.ReadJsonAsync()).GetProperty("summary").GetProperty("assignedAdministratorName").GetString());
        var secondNotifications = await (await second.GetAsync("/api/notifications?unreadOnly=true")).ReadJsonAsync();
        Assert.Contains(secondNotifications.GetProperty("items").EnumerateArray(), n => n.GetProperty("notificationType").GetString() == "Ticket.Assigned" && n.GetProperty("entityId").GetString() == id.ToString());

        // Invalid transitions are rejected with the documented codes.
        var resolveTooEarly = await platform.PostJsonAsync($"/api/platform/support/tickets/{id}/status", new { status = "Resolved", note = "done" });
        Assert.Equal(HttpStatusCode.Conflict, resolveTooEarly.StatusCode);
        Assert.Contains("ticket_transition_invalid", await resolveTooEarly.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var requesterStarts = await alice.PostJsonAsync($"/api/support/tickets/{id}/status", new { status = "Started" });
        Assert.Equal(HttpStatusCode.Conflict, requesterStarts.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await platform.PostJsonAsync($"/api/platform/support/tickets/{id}/status", new { status = "Started" })).StatusCode);
        var pending = await platform.PostJsonAsync($"/api/platform/support/tickets/{id}/status", new { status = "PendingUserResponse", note = "Please send the screenshot." });
        Assert.Equal("PendingUserResponse", StatusOf(await pending.ReadJsonAsync()));

        // The requester is notified of the status change, and their reply moves the ticket back to Started.
        var aliceNotifications = await (await alice.GetAsync("/api/notifications?unreadOnly=true")).ReadJsonAsync();
        var statusNotification = aliceNotifications.GetProperty("items").EnumerateArray().First(n => n.GetProperty("notificationType").GetString() == "Ticket.StatusChanged");
        Assert.Contains("PendingUserResponse", statusNotification.GetProperty("dataJson").GetString(), StringComparison.Ordinal);
        var reply = await alice.PostJsonAsync($"/api/support/tickets/{id}/comments", new { body = "Here is the screenshot." });
        Assert.Equal(HttpStatusCode.Created, reply.StatusCode);
        var afterReply = await (await alice.GetAsync($"/api/support/tickets/{id}")).ReadJsonAsync();
        Assert.Equal("Started", StatusOf(afterReply));
        Assert.Equal(1, afterReply.GetProperty("summary").GetProperty("commentCount").GetInt32());

        // Resolve needs a summary; then the requester can reopen within the window.
        var noSummary = await platform.PostJsonAsync($"/api/platform/support/tickets/{id}/status", new { status = "Resolved" });
        Assert.Contains("ticket_resolution_summary_required", await noSummary.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var resolved = await (await platform.PostJsonAsync($"/api/platform/support/tickets/{id}/status", new { status = "Resolved", note = "Permission granted." })).ReadJsonAsync();
        Assert.Equal("Resolved", StatusOf(resolved));
        Assert.Equal("Permission granted.", resolved.GetProperty("resolutionSummary").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PostJsonAsync($"/api/support/tickets/{id}/comments", new { body = "closed?" })).StatusCode);

        var reopened = await (await alice.PostJsonAsync($"/api/support/tickets/{id}/status", new { status = "Started", note = "Still broken." })).ReadJsonAsync();
        Assert.Equal("Started", StatusOf(reopened));
        Assert.Equal(1, reopened.GetProperty("reopenedCount").GetInt32());

        var timeline = (await (await platform.GetAsync($"/api/platform/support/tickets/{id}/events")).ReadJsonAsync()).EnumerateArray().Select(e => e.GetProperty("eventType").GetString()).ToList();
        Assert.Equal(["TicketCreated", "TicketViewed", "TicketAssigned", "StatusChanged", "StatusChanged", "CommentAdded", "StatusChanged", "StatusChanged", "TicketReopened"], timeline);

        // Platform-side resolution shows up in the dashboard KPIs.
        var dashboard = await (await platform.GetAsync("/api/platform/dashboard")).ReadJsonAsync();
        var kpis = dashboard.GetProperty("kpis").EnumerateArray().ToDictionary(k => k.GetProperty("code").GetString()!, k => k);
        Assert.True(kpis["openTickets"].GetProperty("collected").GetBoolean());
        Assert.True(kpis["openTickets"].GetProperty("value").GetInt64() >= 1);
        Assert.True(kpis["avgFirstResponseMinutes"].GetProperty("collected").GetBoolean());
        Assert.Contains(dashboard.GetProperty("series").EnumerateArray(), s => s.GetProperty("code").GetString() == "ticketsCreated");
    }

    [Fact]
    public async Task Attachments_AreValidated_StoredUnderRandomKeys_AndDownloadedOnlyByAuthorizedPrincipals()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        var ticket = await CreateTicketAsync(alice, "Attachment ticket");
        var id = IdOf(ticket);

        var upload = await UploadAsync(alice, $"/api/support/tickets/{id}/attachments", "error.log", "text/plain", Encoding.UTF8.GetBytes("stack trace line 1\n"));
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var attachment = await upload.ReadJsonAsync();
        var attachmentId = attachment.GetProperty("attachmentId").GetGuid();
        Assert.Equal("error.log", attachment.GetProperty("fileName").GetString());
        Assert.DoesNotContain("storageKey", await upload.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var rejectedType = await UploadAsync(alice, $"/api/support/tickets/{id}/attachments", "malware.exe", "application/octet-stream", new byte[16]);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedType.StatusCode);
        Assert.Contains("attachment_type_not_allowed", await rejectedType.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var tooLarge = await UploadAsync(alice, $"/api/support/tickets/{id}/attachments", "big.txt", "text/plain", new byte[10 * 1024 * 1024 + 1]);
        Assert.True(tooLarge.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge);

        // Download by the requester, by a platform administrator (audited), never by another organization.
        var download = await alice.GetAsync($"/api/support/tickets/{id}/attachments/{attachmentId}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("stack trace line 1\n", await download.Content.ReadAsStringAsync());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);

        using var platform = await LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        Assert.Equal(HttpStatusCode.OK, (await platform.GetAsync($"/api/platform/support/tickets/{id}/attachments/{attachmentId}")).StatusCode);
        using var bob = await LoginAsync(TestData.BobEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/support/tickets/{id}/attachments/{attachmentId}")).StatusCode);

        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var stored = await context.SupportTicketAttachments.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.AttachmentId == attachmentId);
        Assert.Matches("^support-attachments/[0-9]{4}/[0-9]{2}/[0-9a-f]{32}\\.bin$", stored.StorageKey);
        Assert.True(File.Exists(Path.Combine(_storageRoot, stored.StorageKey.Replace('/', Path.DirectorySeparatorChar))));
        Assert.True(await context.AuditLogs.AsNoTracking().AnyAsync(a => a.EventType == "SupportTicket.AttachmentDownloaded" && a.EntityId == id.ToString()));
    }

    [Fact]
    public async Task Notifications_ArePrivateToTheirRecipient_AndCanBeMarkedRead()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        var ticket = await CreateTicketAsync(alice, "Notification ticket");
        using var platform = await LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        await platform.GetAsync($"/api/platform/support/tickets/{IdOf(ticket)}");
        await platform.PostJsonAsync($"/api/platform/support/tickets/{IdOf(ticket)}/comments", new { body = "Looking into it." });

        var mine = await (await alice.GetAsync("/api/notifications?unreadOnly=true")).ReadJsonAsync();
        var comment = mine.GetProperty("items").EnumerateArray().First(n => n.GetProperty("notificationType").GetString() == "Ticket.CommentAdded");
        var notificationId = comment.GetProperty("notificationId").GetGuid();

        using var bob = await LoginAsync(TestData.BobEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostJsonAsync($"/api/notifications/{notificationId}/read", new { })).StatusCode);
        Assert.DoesNotContain((await (await bob.GetAsync("/api/notifications")).ReadJsonAsync()).GetProperty("items").EnumerateArray(), n => n.GetProperty("notificationId").GetGuid() == notificationId);

        Assert.Equal(HttpStatusCode.NoContent, (await alice.PostJsonAsync($"/api/notifications/{notificationId}/read", new { })).StatusCode);
        var marked = await (await alice.PostJsonAsync("/api/notifications/read-all", new { })).ReadJsonAsync();
        Assert.True(marked.GetProperty("marked").GetInt32() >= 0);
        Assert.Equal(0, (await (await alice.GetAsync("/api/notifications/unread-count")).ReadJsonAsync()).GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task ParallelCreates_NeverProduceDuplicateReferences()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        var creates = Enumerable.Range(0, 12).Select(i => CreateTicketAsync(alice, $"Parallel {i}")).ToArray();
        var references = (await Task.WhenAll(creates)).Select(t => t.GetProperty("summary").GetProperty("referenceNumber").GetString()!).ToList();
        Assert.Equal(references.Count, references.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task TicketEvents_AreAppendOnly()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        var ticket = await CreateTicketAsync(alice, "Immutable timeline");
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var row = await context.SupportTicketEvents.IgnoreQueryFilters().FirstAsync(e => e.TicketId == IdOf(ticket));
        row.Note = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        var again = await context.SupportTicketEvents.IgnoreQueryFilters().FirstAsync(e => e.TicketId == IdOf(ticket));
        context.SupportTicketEvents.Remove(again);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string path, string fileName, string contentType, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return await client.PostAsync(new Uri(path, UriKind.Relative), form);
    }
}
