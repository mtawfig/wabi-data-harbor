using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Email;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Entities.Support;
using WabiInsight.Core.Tenancy;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Platform;

/// <summary>
/// P13.52: a visitor of the landing page asks for a demo without an account. The request is stored with consent, the
/// team and the visitor are told, robots and repeats do not multiply it, and only Platform Administrators read and
/// follow it up.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class DemoRequestTests : IDisposable
{
    private const string TeamMailbox = "support@wabitex.com";

    private readonly SqlServerTestDatabase _db;
    private readonly ApiFactory _factory;

    public DemoRequestTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new ApiFactory(db.ConnectionString, new Dictionary<string, string?> { ["Security:DemoRequestRateLimitPerMinute"] = "1000", ["DemoRequests:Enabled"] = "true" });
    }

    public void Dispose() => _factory.Dispose();

    private static string NewEmail() => $"p1352.{Guid.NewGuid():N}@visitor.test";

    private static async Task<HttpClient> AnonymousAsync(WebApplicationFactory<Program> api)
    {
        var client = api.CreateClient();
        await client.RefreshCsrfAsync();
        return client;
    }

    private async Task<HttpClient> AdministratorAsync()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(TestData.AdminEmail, TestData.AdminPassword)).StatusCode);
        return client;
    }

    private static object Form(string email, string? website = null, bool consent = true, string fullName = "Noura Al Mansoori", string pageLanguage = "en", string? message = "We run two SQL Server systems.")
        => new
        {
            fullName,
            email,
            organizationName = "Falcon Logistics",
            countryCode = "ae",
            consent,
            jobTitle = "Head of Operations",
            phone = "+971 50 000 0000",
            industry = "Logistics",
            organizationSize = "201-1000",
            databaseSystems = "SQL Server",
            demoLanguage = "ar",
            preferredTime = "Mornings, Gulf time",
            message,
            pageLanguage,
            website,
        };

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, object form) => client.PostJsonAsync("/api/public/demo-requests", form);

    private async Task<DemoRequest?> FindAsync(string email)
    {
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        return await context.DemoRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Email == email);
    }

    [Fact]
    public async Task SwitchedOff_TheFormIsClosedToEveryone_AndNothingIsStored_WhileEarlierRequestsStay()
    {
        // Off is the default (P13.56): no setting is given here.
        using var closed = new ApiFactory(_db.ConnectionString, new Dictionary<string, string?> { ["Security:DemoRequestRateLimitPerMinute"] = "1000" });
        using var visitor = await AnonymousAsync(closed);
        Assert.False((await (await visitor.GetAsync("/api/public/demo-requests/availability")).ReadJsonAsync()).GetProperty("enabled").GetBoolean());
        var email = $"closed.{Guid.NewGuid():N}@falcon.test";
        Assert.Equal(HttpStatusCode.NotFound, (await SubmitAsync(visitor, Form(email))).StatusCode);
        // Signed-in people are refused the same way: an organization user and a Platform Administrator.
        using var alice = closed.CreateClient();
        await alice.LoginAsync(TestData.AliceEmail, TestData.UserPassword);
        Assert.Equal(HttpStatusCode.NotFound, (await SubmitAsync(alice, Form(email))).StatusCode);
        using var admin = closed.CreateClient();
        await admin.LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        Assert.Equal(HttpStatusCode.NotFound, (await SubmitAsync(admin, Form(email))).StatusCode);
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            Assert.False(await context.DemoRequests.AnyAsync(r => r.Email == email));
        }

        // Nothing is removed: the administrators still open the list of requests received earlier.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/platform/demo-requests")).StatusCode);

        // Switched on, the same address answers that the form is open.
        using var open = _factory.CreateClient();
        Assert.True((await (await open.GetAsync("/api/public/demo-requests/availability")).ReadJsonAsync()).GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Visitor_RequestsADemo_ItIsStoredAndTheTeamAndTheVisitorAreTold()
    {
        var email = NewEmail();
        using var visitor = await AnonymousAsync(_factory);
        var sent = await SubmitAsync(visitor, Form(email, pageLanguage: "ar"));
        Assert.Equal(HttpStatusCode.NoContent, sent.StatusCode);
        Assert.Empty(await sent.Content.ReadAsStringAsync());

        var request = await FindAsync(email);
        Assert.NotNull(request);
        Assert.Equal("Noura Al Mansoori", request.FullName);
        Assert.Equal("Falcon Logistics", request.OrganizationName);
        Assert.Equal("AE", request.CountryCode);
        Assert.Equal(DemoRequestStatuses.New, request.Status);
        Assert.Equal("ar", request.DemoLanguage);
        Assert.Equal("ar", request.PageLanguage);
        Assert.InRange(request.ConsentAt, DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(0, request.RepeatCount);

        // The visitor gets a confirmation in the language of the page; the team gets the details, and a reply goes to the visitor.
        var confirmation = await _factory.Emails.WaitForAsync(email);
        Assert.Equal("ar", confirmation.Language);
        Assert.Contains("وصلنا طلبك", confirmation.Subject, StringComparison.Ordinal);
        var team = await _factory.Emails.WaitForAsync(TeamMailbox, m => m.TextBody.Contains(email, StringComparison.Ordinal));
        Assert.Equal("New demo request: Falcon Logistics", team.Subject);
        Assert.Equal(email, team.ReplyTo);
        Assert.Contains("Head of Operations", team.TextBody, StringComparison.Ordinal);
        Assert.Contains($"/platform/demo-requests/{request.DemoRequestId}", team.TextBody, StringComparison.Ordinal);

        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var id = request.DemoRequestId.ToString();

        // Every Platform Administrator has it in the notification centre; no organization user does.
        var notified = await context.Notifications.AsNoTracking().Where(n => n.NotificationType == NotificationTypes.DemoRequestCreated && n.EntityId == id).ToListAsync();
        Assert.Contains(notified, n => n.RecipientId == _db.Data.AdministratorId);
        Assert.Contains(notified, n => n.RecipientId == _db.Data.SecondAdministratorId);
        Assert.All(notified, n => { Assert.Equal(PrincipalType.PlatformAdministrator, n.RecipientType); Assert.Null(n.OrganizationId); });

        Assert.Equal(DemoRequestEventTypes.Submitted, (await context.DemoRequestEvents.AsNoTracking().SingleAsync(e => e.DemoRequestId == request.DemoRequestId)).EventType);
        Assert.True(await context.AuditLogs.IgnoreQueryFilters().AsNoTracking().AnyAsync(a => a.EventType == AuditEvents.DemoRequestSubmitted && a.EntityId == id));
        var rows = await context.EmailMessages.IgnoreQueryFilters().AsNoTracking().Where(m => m.RecipientEmail == email || m.TemplateCode == EmailTemplateCodes.DemoRequestReceived).ToListAsync();
        Assert.Contains(rows, m => m.TemplateCode == EmailTemplateCodes.DemoRequestConfirmation && m.RecipientEmail == email && m.OrganizationId == null);
        Assert.Contains(rows, m => m.TemplateCode == EmailTemplateCodes.DemoRequestReceived && m.RecipientEmail == TeamMailbox);
    }

    [Fact]
    public async Task IncompleteForm_IsRefusedWithTheFieldsAtFault_AndNothingIsStored()
    {
        var email = NewEmail();
        using var visitor = await AnonymousAsync(_factory);

        var noConsent = await SubmitAsync(visitor, Form(email, consent: false));
        Assert.Equal(HttpStatusCode.BadRequest, noConsent.StatusCode);
        Assert.Equal("required", (await noConsent.ReadJsonAsync()).GetProperty("errors").GetProperty("consent")[0].GetString());

        var empty = await SubmitAsync(visitor, new { fullName = " ", email = "not-an-address", organizationName = "", countryCode = "ZZ", consent = true, industry = "Astrology" });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        var errors = (await empty.ReadJsonAsync()).GetProperty("errors");
        Assert.Equal("required", errors.GetProperty("fullName")[0].GetString());
        Assert.Equal("invalid", errors.GetProperty("email")[0].GetString());
        Assert.Equal("required", errors.GetProperty("organizationName")[0].GetString());
        Assert.Equal("invalid", errors.GetProperty("countryCode")[0].GetString());
        Assert.Equal("invalid", errors.GetProperty("industry")[0].GetString());

        // A line break in a name would become a header in an e-mail: refused.
        var injected = await SubmitAsync(visitor, Form(email, fullName: "Noura\r\nBcc: someone@else.test"));
        Assert.Equal(HttpStatusCode.BadRequest, injected.StatusCode);
        Assert.Equal("invalid", (await injected.ReadJsonAsync()).GetProperty("errors").GetProperty("fullName")[0].GetString());

        Assert.Null(await FindAsync(email));
        Assert.DoesNotContain(_factory.Emails.Messages, m => m.ToEmail == email);
    }

    [Fact]
    public async Task TrapField_AnswersLikeASuccess_AndStoresAndSendsNothing()
    {
        var email = NewEmail();
        using var robot = await AnonymousAsync(_factory);
        var sent = await SubmitAsync(robot, Form(email, website: "https://spam.example"));
        Assert.Equal(HttpStatusCode.NoContent, sent.StatusCode);

        Assert.Null(await FindAsync(email));
        Assert.DoesNotContain(_factory.Emails.Messages, m => m.ToEmail == email || m.TextBody.Contains(email, StringComparison.Ordinal));
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        Assert.True(await context.AuditLogs.IgnoreQueryFilters().AsNoTracking().AnyAsync(a => a.EventType == AuditEvents.DemoRequestDropped));
    }

    [Fact]
    public async Task SameAddressAgain_IsCountedOnTheOpenRequest_NotStoredTwice()
    {
        var email = NewEmail();
        using var visitor = await AnonymousAsync(_factory);
        Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(visitor, Form(email))).StatusCode);
        await _factory.Emails.WaitForAsync(email);
        var again = await SubmitAsync(visitor, Form(email.Replace("p1352", "P1352", StringComparison.Ordinal), message: "Any news?"));
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);

        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var normalized = email.ToUpperInvariant();
        var request = await context.DemoRequests.AsNoTracking().SingleAsync(r => r.NormalizedEmail == normalized);
        Assert.Equal(1, request.RepeatCount);
        Assert.NotNull(request.LastRepeatedAt);
        var events = await context.DemoRequestEvents.AsNoTracking().Where(e => e.DemoRequestId == request.DemoRequestId).ToListAsync();
        Assert.Contains(events, e => e.EventType == DemoRequestEventTypes.Repeated && e.Note == "Any news?");

        // One confirmation only: the repeat sends nothing.
        await Task.Delay(300);
        Assert.Single(_factory.Emails.Messages, m => string.Equals(m.ToEmail, email, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PublicForm_IsLimitedPerClientAddress()
    {
        using var strict = new ApiFactory(_db.ConnectionString, new Dictionary<string, string?> { ["Security:DemoRequestRateLimitPerMinute"] = "2", ["DemoRequests:Enabled"] = "true" });
        using var visitor = await AnonymousAsync(strict);
        Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(visitor, Form(NewEmail()))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(visitor, Form(NewEmail()))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SubmitAsync(visitor, Form(NewEmail()))).StatusCode);
    }

    [Fact]
    public async Task OnlyPlatformAdministrators_ReadAndFollowUpDemoRequests()
    {
        var email = NewEmail();
        using var visitor = await AnonymousAsync(_factory);
        Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(visitor, Form(email))).StatusCode);
        var id = (await FindAsync(email))!.DemoRequestId;

        // Nobody without an administrator's session: not the visitor, not an organization user.
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/platform/demo-requests")).StatusCode);
        using var alice = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await alice.LoginAsync(TestData.AliceEmail, TestData.UserPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/platform/demo-requests")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync($"/api/platform/demo-requests/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PutJsonAsync($"/api/platform/demo-requests/{id}", new { status = "Spam" })).StatusCode);

        using var admin = await AdministratorAsync();
        var list = await (await admin.GetAsync($"/api/platform/demo-requests?status=New&country=AE&search={Uri.EscapeDataString(email)}")).ReadJsonAsync();
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(id, list.GetProperty("items")[0].GetProperty("demoRequestId").GetGuid());
        Assert.True((await (await admin.GetAsync("/api/platform/demo-requests/summary")).ReadJsonAsync()).GetProperty("new").GetInt32() >= 1);

        var detail = await (await admin.GetAsync($"/api/platform/demo-requests/{id}")).ReadJsonAsync();
        Assert.Equal("Falcon Logistics", detail.GetProperty("organizationName").GetString());
        Assert.Equal("Submitted", detail.GetProperty("events")[0].GetProperty("eventType").GetString());

        // A scheduled demo needs its date; a performed one needs when and who attended.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutJsonAsync($"/api/platform/demo-requests/{id}", new { status = "Scheduled" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutJsonAsync($"/api/platform/demo-requests/{id}", new { status = "Performed" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutJsonAsync($"/api/platform/demo-requests/{id}", new { status = "Won" })).StatusCode);

        var when = DateTimeOffset.UtcNow.AddDays(3);
        var scheduled = await admin.PutJsonAsync($"/api/platform/demo-requests/{id}", new
        {
            status = "Scheduled",
            remark = "Asked for Arabic.",
            assignedAdministratorId = _db.Data.SecondAdministratorId,
            scheduledAt = when,
            meetingLocation = "https://meet.example/demo",
            note = "Agreed by phone.",
        });
        Assert.Equal(HttpStatusCode.OK, scheduled.StatusCode);
        Assert.Equal("Second Admin", (await scheduled.ReadJsonAsync()).GetProperty("assignedAdministratorName").GetString());

        var performed = await admin.PutJsonAsync($"/api/platform/demo-requests/{id}", new
        {
            status = "Performed",
            remark = "Asked for Arabic.",
            assignedAdministratorId = _db.Data.SecondAdministratorId,
            scheduledAt = when,
            performedAt = when.AddHours(1),
            presentedBy = "Second Admin",
            attendeeName = "Khalifa Al Nuaimi",
            attendeePosition = "Chief Operating Officer",
            attendeeEmail = "coo@falcon.test",
            attendeeMobile = "+971 50 111 1111",
            impression = "VeryPositive",
            performedRemark = "Wants a pilot on the operations database.",
            nextStep = "Send the proposal",
            followUpDate = "2027-01-10",
        });
        Assert.Equal(HttpStatusCode.OK, performed.StatusCode);
        var after = await performed.ReadJsonAsync();
        Assert.Equal("Performed", after.GetProperty("status").GetString());
        Assert.Equal("Khalifa Al Nuaimi", after.GetProperty("attendeeName").GetString());
        Assert.Equal("2027-01-10", after.GetProperty("followUpDate").GetString());
        var history = after.GetProperty("events").EnumerateArray().Select(e => (Type: e.GetProperty("eventType").GetString(), To: Text(e, "newStatus"), By: Text(e, "actorDisplayName"))).ToList();
        Assert.Equal(("StatusChanged", "Performed", "Platform Admin"), history[0]);
        Assert.Equal(("StatusChanged", "Scheduled", "Platform Admin"), history[1]);
        Assert.Equal("Submitted", history[2].Type);

        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        Assert.Equal(2, await context.AuditLogs.IgnoreQueryFilters().AsNoTracking().CountAsync(a => a.EventType == AuditEvents.DemoRequestUpdated && a.EntityId == id.ToString()));

        // The history cannot be rewritten.
        var first = await context.DemoRequestEvents.FirstAsync(e => e.DemoRequestId == id);
        first.Note = "changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task PersonalDetails_AreErasedAfterTheRetentionPeriod_UnlessTheRequestBecameACustomer()
    {
        var old = DateTimeOffset.UtcNow.AddMonths(-25);
        var lost = NewRequest(old, DemoRequestStatuses.NotInterested);
        var won = NewRequest(old, DemoRequestStatuses.Converted);
        var recent = NewRequest(DateTimeOffset.UtcNow.AddMonths(-3), DemoRequestStatuses.Contacted);
        await using (var seed = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            seed.DemoRequests.AddRange(lost, won, recent);
            seed.DemoRequestEvents.Add(new DemoRequestEvent { EventId = Ids.New(), DemoRequestId = lost.DemoRequestId, EventType = DemoRequestEventTypes.Submitted, CreatedAt = old });
            await seed.SaveChangesAsync();
        }

        // Opening the list is enough: the housekeeping runs with it.
        using var admin = await AdministratorAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/platform/demo-requests")).StatusCode);

        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var erased = await context.DemoRequests.AsNoTracking().SingleAsync(r => r.DemoRequestId == lost.DemoRequestId);
        Assert.NotNull(erased.ErasedAt);
        Assert.Equal("(erased)", erased.FullName);
        Assert.Equal("(erased)", erased.Email);
        Assert.Null(erased.Phone);
        Assert.Null(erased.IpAddress);
        Assert.Null(erased.AttendeeName);
        Assert.Equal("AE", erased.CountryCode);
        Assert.Equal(DemoRequestStatuses.NotInterested, erased.Status);
        Assert.False(await context.DemoRequestEvents.AsNoTracking().AnyAsync(e => e.DemoRequestId == lost.DemoRequestId));
        Assert.Null((await context.DemoRequests.AsNoTracking().SingleAsync(r => r.DemoRequestId == won.DemoRequestId)).ErasedAt);
        Assert.Null((await context.DemoRequests.AsNoTracking().SingleAsync(r => r.DemoRequestId == recent.DemoRequestId)).ErasedAt);

        // An erased request can no longer be edited.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutJsonAsync($"/api/platform/demo-requests/{lost.DemoRequestId}", new { status = "Contacted" })).StatusCode);
    }

    /// <summary>The API leaves empty fields out of its answers.</summary>
    private static string? Text(System.Text.Json.JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static DemoRequest NewRequest(DateTimeOffset createdAt, string status)
    {
        var email = NewEmail();
        return new DemoRequest
        {
            DemoRequestId = Ids.New(),
            FullName = "Old Visitor",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            OrganizationName = "Old Organization",
            CountryCode = "AE",
            Phone = "+971 50 222 2222",
            IpAddress = "203.0.113.7",
            AttendeeName = "Someone",
            DemoLanguage = "en",
            PageLanguage = "en",
            ConsentAt = createdAt,
            CreatedAt = createdAt,
            Status = status,
        };
    }
}
