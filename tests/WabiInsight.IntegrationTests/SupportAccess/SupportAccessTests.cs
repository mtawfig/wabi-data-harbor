using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Support;
using WabiInsight.Core.Tenancy;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;
using WabiInsight.IntegrationTests.Email;

namespace WabiInsight.IntegrationTests.SupportAccess;

[Collection(DatabaseCollectionDefinition.Name)]
public sealed class SupportAccessTests : IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly ApiFactory _factory;

    public SupportAccessTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new ApiFactory(db.ConnectionString);
    }

    public void Dispose() => _factory.Dispose();

    private async Task<HttpClient> LoginAsync(string email, string password = TestData.UserPassword)
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, password)).StatusCode);
        return client;
    }

    /// <summary>A second Platform Administrator of this test's own (others reset the seeded one's password), signed in.</summary>
    private async Task<HttpClient> NewPlatformAdministratorAsync(HttpClient platform)
    {
        var email = $"other.admin.{Guid.NewGuid():N}@wabitex.test";
        var create = await platform.PostJsonAsync("/api/platform/administrators", new { email, fullName = "Other Admin" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var temporary = Email.RecordingEmailSender.TemporaryPasswordOf(await _factory.Emails.WaitForAsync(email));
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, temporary)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostJsonAsync("/api/auth/change-password", new { currentPassword = temporary, newPassword = "Other#Admin#Strong#2026" })).StatusCode);
        await client.RefreshCsrfAsync();
        return client;
    }

    private static async Task SetSecondApprovalAsync(HttpClient admin, bool value)
    {
        var profile = await (await admin.GetAsync("/api/organization")).ReadJsonAsync();
        var update = await admin.PutAsJsonAsync(new Uri("/api/organization", UriKind.Relative), new
        {
            organizationName = profile.GetProperty("organizationName").GetString(), organizationNameAr = profile.GetProperty("organizationNameAr").GetString(),
            timeZone = profile.GetProperty("timeZone").GetString(), defaultLanguage = profile.GetProperty("defaultLanguage").GetString(), requireSecondAdminApprovalForSupportAccess = value,
        });
        Assert.True(update.IsSuccessStatusCode, await update.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> CreateTicketAsync(HttpClient client, string subject)
    {
        var response = await client.PostJsonAsync("/api/support/tickets", new { ticketType = "Request", impact = "Medium", subject, description = "Please help with configuration.", requiresSupportAccess = true });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("summary").GetProperty("ticketId").GetGuid();
    }

    private static async Task<JsonElement> ProposeAsync(HttpClient platform, Guid ticketId, object[] operations, int? expiryHours = null)
    {
        var response = await platform.PostJsonAsync($"/api/platform/support/tickets/{ticketId}/access-grants", new { operations, reason = "Requested in the ticket.", expiryHours });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static Task<HttpResponseMessage> ExecuteAsync(HttpClient platform, string operation, string? targetReference, object? payload)
        => platform.PostJsonAsync("/api/platform/support/session/execute", new { operation, targetReference, payload });

    [Fact]
    public async Task FullWorkflow_Propose_Approve_Enter_Execute_Complete_IsScopedAndAudited()
    {
        using var alice = await LoginAsync(TestData.AliceEmail); // OrganizationAdmin of A, also the requester
        await SetSecondApprovalAsync(alice, false);
        var ticketId = await CreateTicketAsync(alice, "Need a new analyst account");
        using var platform = await LoginAsync(TestData.AdminEmail, TestData.AdminPassword);

        // Nothing is possible before a grant: no session, execute denied.
        await platform.PostJsonAsync("/api/platform/support/session/exit", new { }); // other test classes may leave a session open
        Assert.Equal(HttpStatusCode.NoContent, (await platform.GetAsync("/api/platform/support/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ExecuteAsync(platform, "CreateOrganizationUser", null, new { email = "x@org-a.test", fullName = "X" })).StatusCode);

        // A ticket alone grants nothing (P13.26): before any grant the targets are the person who asked and the
        // standard roles. Not the organization's user list, not its own roles, not its table names.
        var targets = await (await platform.GetAsync($"/api/platform/support/tickets/{ticketId}/targets")).ReadJsonAsync();
        Assert.Equal(_db.Data.UserA1Id, Assert.Single(targets.GetProperty("users").EnumerateArray()).GetProperty("userId").GetGuid());
        Assert.Contains(targets.GetProperty("roles").EnumerateArray(), r => r.GetProperty("roleCode").GetString() == "Analyst");
        // A role the organization defined itself is not offered; it is added by its exact code when the ticket names it.
        var ownRole = await (await alice.PostJsonAsync("/api/roles", new { roleCode = $"Own{Guid.NewGuid():N}"[..14], nameEn = "Own role", nameAr = "دور خاص", description = (string?)null })).ReadJsonAsync();
        var ownRoleId = ownRole.GetProperty("roleId").GetGuid();
        var offeredRoles = (await (await platform.GetAsync($"/api/platform/support/tickets/{ticketId}/targets")).ReadJsonAsync()).GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("roleId").GetGuid()).ToList();
        Assert.DoesNotContain(ownRoleId, offeredRoles);
        var standard = (await (await alice.GetAsync("/api/roles")).ReadJsonAsync()).EnumerateArray().Where(r => r.GetProperty("isSystem").GetBoolean() || (r.TryGetProperty("templateVersion", out var version) && version.ValueKind == JsonValueKind.Number)).Select(r => r.GetProperty("roleId").GetGuid()).ToList();
        Assert.Equal(standard.Order(), offeredRoles.Order());
        Assert.Equal(ownRoleId, (await (await platform.GetAsync($"/api/platform/support/tickets/{ticketId}/targets/role?code={Uri.EscapeDataString(ownRole.GetProperty("roleCode").GetString()!)}")).ReadJsonAsync()).GetProperty("roleId").GetGuid());
        Assert.Empty(targets.GetProperty("objects").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync($"/api/platform/support/tickets/{ticketId}/targets")).StatusCode);

        // Someone the ticket names is added by exact address: one user of this organization, or nothing.
        var found = await platform.GetAsync($"/api/platform/support/tickets/{ticketId}/targets/user?email={Uri.EscapeDataString(TestData.ChangerEmail.ToUpperInvariant())}");
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal(_db.Data.UserAChangerId, (await found.ReadJsonAsync()).GetProperty("userId").GetGuid());
        foreach (var miss in new[] { TestData.BobEmail, "changer", "%@org-a.test", "nobody@org-a.test", "" })
        {
            var notFound = await platform.GetAsync($"/api/platform/support/tickets/{ticketId}/targets/user?email={Uri.EscapeDataString(miss)}");
            Assert.True(notFound.StatusCode == HttpStatusCode.NotFound, $"'{miss}': {(int)notFound.StatusCode}");
            Assert.Contains("support_target_not_found", await notFound.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync($"/api/platform/support/tickets/{ticketId}/targets/user?email={Uri.EscapeDataString(TestData.ChangerEmail)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await platform.GetAsync($"/api/platform/support/tickets/{ticketId}/targets/role?code={Uri.EscapeDataString("NoSuchRole")}")).StatusCode);
        var roleByCode = await platform.GetAsync($"/api/platform/support/tickets/{ticketId}/targets/role?code=Analyst");
        Assert.Equal("Analyst", (await roleByCode.ReadJsonAsync()).GetProperty("roleCode").GetString());

        var analystRoleId = targets.GetProperty("roles").EnumerateArray().Single(r => r.GetProperty("roleCode").GetString() == "Analyst").GetProperty("roleId").GetGuid();

        var grant = await ProposeAsync(platform, ticketId, new object[]
        {
            new { operation = "CreateOrganizationUser", targetReferences = new[] { analystRoleId.ToString(), "email:Assisted@org-a.test" } }, // the only role and the only address
            new { operation = "ResetOrganizationUserPassword", targetReferences = new[] { _db.Data.UserAChangerId.ToString() } },
            new { operation = "ConfigureRole", targetReferences = new[] { "new" } },
        }, expiryHours: 2);
        var grantId = grant.GetProperty("grantId").GetGuid();
        Assert.Equal("Proposed", grant.GetProperty("status").GetString());
        Assert.Equal(3, grant.GetProperty("operations").GetArrayLength());

        // Entering before approval is refused; the organization administrator sees the pending grant and approves it.
        var early = await platform.PostJsonAsync($"/api/platform/support/access-grants/{grantId}/enter", new { });
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        var pending = await (await alice.GetAsync("/api/support/access-grants?pending=true")).ReadJsonAsync();
        var mine = pending.GetProperty("items").EnumerateArray().Single(g => g.GetProperty("grantId").GetGuid() == grantId);
        Assert.Contains("approve", mine.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));
        var aliceNotifications = await (await alice.GetAsync("/api/notifications?unreadOnly=true")).ReadJsonAsync();
        Assert.Contains(aliceNotifications.GetProperty("items").EnumerateArray(), n => n.GetProperty("notificationType").GetString() == "SupportAccess.Proposed");

        var approve = await alice.PostJsonAsync($"/api/support/access-grants/{grantId}/approve", new { note = "Go ahead." });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var approved = await approve.ReadJsonAsync();
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        var expiresAt = approved.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange(expiresAt, DateTimeOffset.UtcNow.AddHours(1.9), DateTimeOffset.UtcNow.AddHours(2.1));

        // Enter support mode; the session names the grant and the operations.
        var enter = await platform.PostJsonAsync($"/api/platform/support/access-grants/{grantId}/enter", new { });
        Assert.Equal(HttpStatusCode.OK, enter.StatusCode);
        var session = await (await platform.GetAsync("/api/platform/support/session")).ReadJsonAsync();
        Assert.Equal(grantId, session.GetProperty("grant").GetProperty("grantId").GetGuid());
        Assert.Equal("Active", session.GetProperty("grant").GetProperty("status").GetString());

        // Under the approved grant the targets also hold what the grant names: the user to reset, still not the list.
        var underGrant = await (await platform.GetAsync($"/api/platform/support/tickets/{ticketId}/targets")).ReadJsonAsync();
        Assert.Equal(new[] { _db.Data.UserA1Id, _db.Data.UserAChangerId }.Order(), underGrant.GetProperty("users").EnumerateArray().Select(u => u.GetProperty("userId").GetGuid()).Order());
        Assert.Empty(underGrant.GetProperty("objects").EnumerateArray()); // this grant does not cover table permissions

        // An account is created only for the address the organization approved.
        var otherAddress = await ExecuteAsync(platform, "CreateOrganizationUser", null, new { email = "someone.else@org-a.test", fullName = "Someone Else", preferredLanguage = "en", roleCodes = new[] { "Analyst" } });
        Assert.Equal(HttpStatusCode.Forbidden, otherAddress.StatusCode);
        Assert.Contains("support_user_email_not_granted", await otherAddress.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Null(await RoleCodesOfAsync("someone.else@org-a.test"));

        // Permitted: create a user (temporary password emailed), reset the listed user, create a role.
        var create = await ExecuteAsync(platform, "CreateOrganizationUser", null, new { email = "assisted@org-a.test", fullName = "Assisted User", preferredLanguage = "en", roleCodes = new[] { "Analyst" } });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await create.ReadJsonAsync();
        Assert.Equal("Succeeded", created.GetProperty("outcome").GetString());
        Assert.Equal("a***d@org-a.test", created.GetProperty("deliveredTo").GetString());
        Assert.DoesNotContain("temporaryPassword", await create.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var email = await _factory.Emails.WaitForAsync("assisted@org-a.test");
        var temporary = RecordingEmailSender.TemporaryPasswordOf(email);
        using var assisted = _factory.CreateClient();
        var login = await assisted.LoginAsync("assisted@org-a.test", temporary);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(_db.Data.OrganizationAId, (await login.ReadJsonAsync()).GetProperty("organizationId").GetGuid());

        Assert.Equal(HttpStatusCode.OK, (await ExecuteAsync(platform, "ResetOrganizationUserPassword", _db.Data.UserAChangerId.ToString(), null)).StatusCode);
        await _factory.Emails.WaitForAsync(TestData.ChangerEmail);

        var role = await ExecuteAsync(platform, "ConfigureRole", "new", new { roleCode = "SupportCreated", nameEn = "Support Created", nameAr = "أنشأه الدعم", isActive = true });
        Assert.Equal(HttpStatusCode.OK, role.StatusCode);
        var roles = await (await alice.GetAsync("/api/roles")).ReadJsonAsync();
        Assert.Contains(roles.EnumerateArray(), r => r.GetProperty("roleCode").GetString() == "SupportCreated");

        // Outside the scope: a user not listed, an operation not granted, an unknown operation.
        var otherUser = await ExecuteAsync(platform, "ResetOrganizationUserPassword", _db.Data.UserA1Id.ToString(), null);
        Assert.Equal(HttpStatusCode.Forbidden, otherUser.StatusCode);
        Assert.Contains("support_target_not_granted", await otherUser.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var notGranted = await ExecuteAsync(platform, "UpdateOrganizationSettings", null, new { organizationName = "Hacked" });
        Assert.Contains("support_operation_not_granted", await notGranted.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await ExecuteAsync(platform, "DropEverything", null, null)).StatusCode);

        // Complete: session ends, nothing more is possible.
        var complete = await platform.PostJsonAsync($"/api/platform/support/access-grants/{grantId}/complete", new { note = "Done." });
        Assert.Equal("Completed", (await complete.ReadJsonAsync()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await platform.GetAsync("/api/platform/support/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ExecuteAsync(platform, "CreateOrganizationUser", null, new { email = "late@org-a.test", fullName = "Late" })).StatusCode);

        // Timeline and audit carry the whole story.
        var timeline = (await (await platform.GetAsync($"/api/platform/support/tickets/{ticketId}/events")).ReadJsonAsync()).EnumerateArray().Select(e => e.GetProperty("eventType").GetString()).ToList();
        Assert.Contains("SupportAccessProposed", timeline);
        Assert.Contains("SupportAccessApproved", timeline);
        Assert.Contains("SupportSessionStarted", timeline);
        Assert.Equal(3, timeline.Count(e => e == "SupportOperationExecuted"));
        Assert.Contains("SupportSessionEnded", timeline);
        Assert.Contains("SupportAccessCompleted", timeline);
        var executed = (await (await platform.GetAsync($"/api/platform/support/tickets/{ticketId}/events")).ReadJsonAsync()).EnumerateArray().First(e => e.GetProperty("eventType").GetString() == "SupportOperationExecuted").GetProperty("detailsJson").GetString()!;
        Assert.Contains(grantId.ToString(), executed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CreateOrganizationUser", executed, StringComparison.Ordinal);
        Assert.DoesNotContain(temporary, executed, StringComparison.Ordinal);

        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var audit = await context.AuditLogs.AsNoTracking().Where(a => a.EntityId == grantId.ToString()).Select(a => a.EventType).ToListAsync();
        Assert.Contains("SupportAccess.Proposed", audit);
        Assert.Contains("SupportAccess.Approved", audit);
        Assert.Contains("SupportAccess.OperationExecuted", audit);
        Assert.Contains("SupportAccess.OperationDenied", audit);
        Assert.Contains("SupportAccess.Completed", audit);
        var everything = string.Join("\n", await context.AuditLogs.AsNoTracking().Where(a => a.OrganizationId == _db.Data.OrganizationAId).Select(a => a.DetailsJson).ToListAsync());
        Assert.DoesNotContain(temporary, everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateOrganizationUser_AssignsOnlyTheRolesTheOrganizationApproved()
    {
        using var alice = await LoginAsync(TestData.AliceEmail); // OrganizationAdmin of A
        await SetSecondApprovalAsync(alice, false);
        var ticketId = await CreateTicketAsync(alice, "Please create an analyst account");
        using var platform = await LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        await platform.PostJsonAsync("/api/platform/support/session/exit", new { });

        // The catalog tells the proposal screen that this operation's targets are the roles the new user may be given.
        var catalog = (await (await platform.GetAsync("/api/platform/support/operations")).ReadJsonAsync()).EnumerateArray().ToList();
        var definition = catalog.Single(o => o.GetProperty("code").GetString() == "CreateOrganizationUser");
        Assert.Equal("AssignableRole", definition.GetProperty("targetType").GetString());
        Assert.False(definition.GetProperty("requiresTarget").GetBoolean());
        Assert.Contains("limited to the ones listed in the grant", definition.GetProperty("description").GetString(), StringComparison.Ordinal);

        var targets = await (await platform.GetAsync($"/api/platform/support/tickets/{ticketId}/targets")).ReadJsonAsync();
        var analyst = targets.GetProperty("roles").EnumerateArray().Single(r => r.GetProperty("roleCode").GetString() == "Analyst");
        var analystRoleId = analyst.GetProperty("roleId").GetGuid();

        var tag = Guid.NewGuid().ToString("N")[..8];
        var approvedEmail = $"approved.{tag}@org-a.test";
        var malformedEmail = $"malformed.{tag}@org-a.test";

        // A role of another organization cannot be listed.
        var crossOrg = await platform.PostJsonAsync($"/api/platform/support/tickets/{ticketId}/access-grants", new { operations = new[] { new { operation = "CreateOrganizationUser", targetReferences = new[] { _db.Data.RoleAdminBId.ToString(), $"email:{approvedEmail}" } } }, reason = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, crossOrg.StatusCode);
        Assert.Contains("support_target_not_in_organization", await crossOrg.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // A proposal to create a user has to say for whom (P13.26): no address, or something that is not one, is refused.
        var noAddress = await platform.PostJsonAsync($"/api/platform/support/tickets/{ticketId}/access-grants", new { operations = new[] { new { operation = "CreateOrganizationUser", targetReferences = new[] { analystRoleId.ToString() } } }, reason = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, noAddress.StatusCode);
        Assert.Contains("support_new_user_email_required", await noAddress.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var badAddress = await platform.PostJsonAsync($"/api/platform/support/tickets/{ticketId}/access-grants", new { operations = new[] { new { operation = "CreateOrganizationUser", targetReferences = new[] { "email:not-an-address" } } }, reason = "x" });
        Assert.Contains("support_new_user_email_invalid", await badAddress.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var misplaced = await platform.PostJsonAsync($"/api/platform/support/tickets/{ticketId}/access-grants", new { operations = new[] { new { operation = "ConfigureRole", targetReferences = new[] { "new", "email:x@org-a.test" } } }, reason = "x" });
        Assert.Contains("support_target_not_applicable", await misplaced.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // 1. A grant that lists the Analyst role and the address: the approver sees exactly which role support may assign and to whom, in both languages.
        var grantId = (await ProposeAsync(platform, ticketId, new object[] { new { operation = "CreateOrganizationUser", targetReferences = new[] { analystRoleId.ToString(), $"email: {approvedEmail.ToUpperInvariant()} ", $"email:{approvedEmail}", $"email:{malformedEmail}" } } })).GetProperty("grantId").GetGuid();
        var pending = await (await alice.GetAsync($"/api/support/access-grants/{grantId}")).ReadJsonAsync();
        var operation = pending.GetProperty("operations").EnumerateArray().Single();
        Assert.Equal("CreateOrganizationUser", operation.GetProperty("operation").GetString());
        // One spelling per address: the same address typed twice is stored once.
        Assert.Equal(new[] { analystRoleId.ToString(), $"email:{approvedEmail}", $"email:{malformedEmail}" }, operation.GetProperty("targetReferences").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal(approvedEmail, pending.GetProperty("targetLabels").GetProperty($"email:{approvedEmail}").GetString());
        Assert.Equal(approvedEmail, pending.GetProperty("targetLabelsAr").GetProperty($"email:{approvedEmail}").GetString());
        Assert.Equal($"{analyst.GetProperty("nameEn").GetString()} (Analyst)", pending.GetProperty("targetLabels").GetProperty(analystRoleId.ToString()).GetString());
        Assert.Equal($"{analyst.GetProperty("nameAr").GetString()} (Analyst)", pending.GetProperty("targetLabelsAr").GetProperty(analystRoleId.ToString()).GetString());
        Assert.Contains("approve", pending.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/support/access-grants/{grantId}/approve", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await platform.PostJsonAsync($"/api/platform/support/access-grants/{grantId}/enter", new { })).StatusCode);

        // The approved role is assigned, to the approved address.
        var approved = await ExecuteAsync(platform, "CreateOrganizationUser", null, new { email = approvedEmail, fullName = "Approved Analyst", roleCodes = new[] { "Analyst" } });
        Assert.True(approved.StatusCode == HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "Analyst" }, await RoleCodesOfAsync(approvedEmail));

        // Any other role is refused: nothing is created, whoever's mailbox the request names.
        foreach (var roleCodes in new[] { new[] { "Management" }, new[] { "OrganizationAdmin" }, new[] { "Analyst", "Management" }, new[] { "management" }, new[] { "NoSuchRole" } })
        {
            var refusedEmail = $"refused.{Guid.NewGuid():N}@elsewhere.test";
            var refused = await ExecuteAsync(platform, "CreateOrganizationUser", null, new { email = refusedEmail, fullName = "Refused", roleCodes });
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Contains("support_role_not_granted", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Null(await RoleCodesOfAsync(refusedEmail));
        }

        // A payload that is not a list of role codes is not read as "no roles".
        var malformed = await ExecuteAsync(platform, "CreateOrganizationUser", null, new { email = malformedEmail, fullName = "Malformed", roleCodes = "Management" });
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Null(await RoleCodesOfAsync(malformedEmail));

        // The denial is audited like any other out-of-scope operation, with the roles that were asked for.
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            var denials = await context.AuditLogs.AsNoTracking().Where(a => a.EntityId == grantId.ToString() && a.EventType == "SupportAccess.OperationDenied").Select(a => new { a.OrganizationId, a.DetailsJson }).ToListAsync();
            Assert.Equal(5, denials.Count);
            Assert.All(denials, d =>
            {
                Assert.Equal(_db.Data.OrganizationAId, d.OrganizationId);
                Assert.Contains("support_role_not_granted", d.DetailsJson, StringComparison.Ordinal);
                Assert.Contains("CreateOrganizationUser", d.DetailsJson, StringComparison.Ordinal);
            });
            Assert.Contains(denials, d => d.DetailsJson!.Contains("\"Management\"", StringComparison.Ordinal));
            Assert.Contains(denials, d => d.DetailsJson!.Contains("\"OrganizationAdmin\"", StringComparison.Ordinal));
        }

        Assert.Equal(HttpStatusCode.OK, (await platform.PostJsonAsync($"/api/platform/support/access-grants/{grantId}/complete", new { note = "Done." })).StatusCode);

        // 2. A grant without role targets: a user without roles only.
        var bareEmail = $"bare.{tag}@org-a.test";
        var bareGrantId = (await ProposeAsync(platform, ticketId, new object[] { new { operation = "CreateOrganizationUser", targetReferences = new[] { $"email:{bareEmail}" } } })).GetProperty("grantId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/support/access-grants/{bareGrantId}/approve", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await platform.PostJsonAsync($"/api/platform/support/access-grants/{bareGrantId}/enter", new { })).StatusCode);
        var withRole = await ExecuteAsync(platform, "CreateOrganizationUser", null, new { email = $"withrole.{tag}@org-a.test", fullName = "With Role", roleCodes = new[] { "Analyst" } });
        Assert.Equal(HttpStatusCode.Forbidden, withRole.StatusCode);
        Assert.Contains("support_role_not_granted", await withRole.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Null(await RoleCodesOfAsync($"withrole.{tag}@org-a.test"));
        var bare = await ExecuteAsync(platform, "CreateOrganizationUser", null, new { email = bareEmail, fullName = "No Roles", roleCodes = Array.Empty<string>() });
        Assert.True(bare.StatusCode == HttpStatusCode.OK, await bare.Content.ReadAsStringAsync());
        Assert.Empty((await RoleCodesOfAsync(bareEmail))!);
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            Assert.Equal(1, await context.AuditLogs.AsNoTracking().CountAsync(a => a.EntityId == bareGrantId.ToString() && a.EventType == "SupportAccess.OperationDenied"));
        }

        Assert.Equal(HttpStatusCode.OK, (await platform.PostJsonAsync($"/api/platform/support/access-grants/{bareGrantId}/complete", new { note = "Done." })).StatusCode);

        // Organization B sees neither grant and none of the created users.
        using var bob = await LoginAsync(TestData.BobEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/support/access-grants/{grantId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/support/access-grants/{bareGrantId}")).StatusCode);
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            var created = await context.OrganizationUsers.IgnoreQueryFilters().Where(u => u.Email == approvedEmail || u.Email == bareEmail).Select(u => u.OrganizationId).ToListAsync();
            Assert.Equal(new[] { _db.Data.OrganizationAId, _db.Data.OrganizationAId }, created);
        }
    }

    /// <summary>Role codes of the user with this email, or null when no such user exists.</summary>
    private async Task<List<string>?> RoleCodesOfAsync(string email)
    {
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var user = await context.OrganizationUsers.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            var normalized = email.Trim().ToUpperInvariant();
            Assert.False(await context.LoginIdentities.IgnoreQueryFilters().AnyAsync(l => l.NormalizedEmail == normalized));
            return null;
        }

        return await context.UserRoles.IgnoreQueryFilters().Where(ur => ur.UserId == user.UserId)
            .Join(context.Roles.IgnoreQueryFilters(), ur => ur.RoleId, r => r.RoleId, (ur, r) => r.RoleCode)
            .OrderBy(c => c).ToListAsync();
    }

    [Fact]
    public async Task SecondAdminApproval_TenantBoundaries_AndTargetsOutsideTheOrganization()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        var profile = await (await alice.GetAsync("/api/organization")).ReadJsonAsync();
        var update = await alice.PutAsJsonAsync(new Uri("/api/organization", UriKind.Relative), new
        {
            organizationName = profile.GetProperty("organizationName").GetString(), organizationNameAr = profile.GetProperty("organizationNameAr").GetString(),
            timeZone = profile.GetProperty("timeZone").GetString(), defaultLanguage = profile.GetProperty("defaultLanguage").GetString(), requireSecondAdminApprovalForSupportAccess = true,
        });
        Assert.True(update.IsSuccessStatusCode, await update.Content.ReadAsStringAsync());
        Assert.True((await update.ReadJsonAsync()).GetProperty("requireSecondAdminApprovalForSupportAccess").GetBoolean());

        var ticketId = await CreateTicketAsync(alice, "Second approval needed");
        using var platform = await LoginAsync(TestData.AdminEmail, TestData.AdminPassword);

        // Targets must belong to the ticket's organization.
        var crossOrg = await platform.PostJsonAsync($"/api/platform/support/tickets/{ticketId}/access-grants", new { operations = new[] { new { operation = "ResetOrganizationUserPassword", targetReferences = new[] { _db.Data.UserB1Id.ToString() } } }, reason = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, crossOrg.StatusCode);
        Assert.Contains("support_target_not_in_organization", await crossOrg.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var grant = await ProposeAsync(platform, ticketId, new object[] { new { operation = "UpdateOrganizationSettings", targetReferences = Array.Empty<string>() } });
        var grantId = grant.GetProperty("grantId").GetGuid();

        // The requester is an OrganizationAdmin but the organization requires a different administrator.
        var self = await alice.PostJsonAsync($"/api/support/access-grants/{grantId}/approve", new { });
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Contains("support_second_admin_required", await self.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var visible = await (await alice.GetAsync($"/api/support/access-grants/{grantId}")).ReadJsonAsync();
        Assert.DoesNotContain("approve", visible.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));

        // Another organization's administrator cannot even see the grant; organization A users who are not admins cannot approve.
        using var bob = await LoginAsync(TestData.BobEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/support/access-grants/{grantId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostJsonAsync($"/api/support/access-grants/{grantId}/approve", new { })).StatusCode); // not an OrganizationAdmin

        // A second OrganizationAdmin approves; the platform administrator can then update settings but not users.
        var secondEmail = $"second.admin.{Guid.NewGuid():N}@org-a.test";
        var second = await alice.PostJsonAsync("/api/users", new { email = secondEmail, fullName = "Second Admin", preferredLanguage = "en", roleCodes = new[] { "OrganizationAdmin" } });
        Assert.True(second.StatusCode == HttpStatusCode.Created, await second.Content.ReadAsStringAsync());
        var temporary = RecordingEmailSender.TemporaryPasswordOf(await _factory.Emails.WaitForAsync(secondEmail));
        using var secondAdmin = _factory.CreateClient();
        await secondAdmin.LoginAsync(secondEmail, temporary);
        await secondAdmin.PostJsonAsync("/api/auth/change-password", new { currentPassword = temporary, newPassword = "Second#Admin#Strong#2026" });
        await secondAdmin.RefreshCsrfAsync();
        var approve = await secondAdmin.PostJsonAsync($"/api/support/access-grants/{grantId}/approve", new { expiryHours = 1 });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await platform.PostJsonAsync($"/api/platform/support/access-grants/{grantId}/enter", new { })).StatusCode);
        var settings = await ExecuteAsync(platform, "UpdateOrganizationSettings", null, new { description = "Updated by support" });
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        Assert.Equal("Updated by support", (await (await alice.GetAsync("/api/organization")).ReadJsonAsync()).GetProperty("description").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await ExecuteAsync(platform, "CreateOrganizationUser", null, new { email = "nope@org-a.test", fullName = "Nope" })).StatusCode);

        // Any OrganizationAdmin revokes immediately: the session ends and execution stops.
        var revoke = await alice.PostJsonAsync($"/api/support/access-grants/{grantId}/revoke", new { note = "Enough." });
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal("Revoked", (await revoke.ReadJsonAsync()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await platform.GetAsync("/api/platform/support/session")).StatusCode);
        var afterRevoke = await ExecuteAsync(platform, "UpdateOrganizationSettings", null, new { description = "Too late" });
        Assert.Equal(HttpStatusCode.Forbidden, afterRevoke.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await platform.PostJsonAsync($"/api/platform/support/access-grants/{grantId}/enter", new { })).StatusCode);

        // Restore the organization setting for other tests.
        await alice.PutAsJsonAsync(new Uri("/api/organization", UriKind.Relative), new
        {
            organizationName = profile.GetProperty("organizationName").GetString(), organizationNameAr = profile.GetProperty("organizationNameAr").GetString(),
            timeZone = profile.GetProperty("timeZone").GetString(), defaultLanguage = profile.GetProperty("defaultLanguage").GetString(), requireSecondAdminApprovalForSupportAccess = false,
        });
    }

    [Fact]
    public async Task AuditPage_WithholdsAnOrganizationsDetails_UnlessTheReaderHoldsAnApprovedGrantForIt()
    {
        using var alice = await LoginAsync(TestData.AliceEmail); // OrganizationAdmin of A
        // An Organization Administrator of B, added for this test (the seeded user of B holds no role).
        var bAdminEmail = $"audit.admin.{Guid.NewGuid():N}@org-b.test";
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            var userId = Ids.New();
            context.OrganizationUsers.Add(TestData.NewUser(userId, _db.Data.OrganizationBId, bAdminEmail, "B Audit Admin"));
            context.LoginIdentities.Add(TestData.Identity(bAdminEmail, PrincipalType.OrganizationUser, userId, _db.Data.OrganizationBId, DateTimeOffset.UtcNow));
            context.UserRoles.Add(new UserRole { OrganizationId = _db.Data.OrganizationBId, UserId = userId, RoleId = _db.Data.RoleAdminBId, AssignedAt = DateTimeOffset.UtcNow, AssignedByAdministratorId = _db.Data.AdministratorId });
            await context.SaveChangesAsync();
        }

        using var bob = await LoginAsync(bAdminEmail);
        await SetSecondApprovalAsync(alice, false);
        using var platform = await LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        using var second = await NewPlatformAdministratorAsync(platform);
        await platform.PostJsonAsync("/api/platform/support/session/exit", new { });

        // Each organization defines a role of its own: the code is the organization's configuration.
        var codeA = $"Pay{Guid.NewGuid():N}"[..14];
        var codeB = $"Pay{Guid.NewGuid():N}"[..14];
        async Task<Guid> OwnRoleAsync(HttpClient administrator, string code)
        {
            var created = await administrator.PostJsonAsync("/api/roles", new { roleCode = code, nameEn = "Payroll", nameAr = "رواتب", description = (string?)null });
            Assert.True(created.IsSuccessStatusCode, $"{(int)created.StatusCode}: {await created.Content.ReadAsStringAsync()}");
            return (await created.ReadJsonAsync()).GetProperty("roleId").GetGuid();
        }

        var roleA = await OwnRoleAsync(alice, codeA);
        var roleB = await OwnRoleAsync(bob, codeB);

        async Task<JsonElement> RowAsync(HttpClient reader, Guid organizationId, Guid roleId)
        {
            var page = await (await reader.GetAsync($"/api/platform/audit?organizationId={organizationId}&eventType={Uri.EscapeDataString(AuditEvents.RoleSaved)}&pageSize=200")).ReadJsonAsync();
            return page.GetProperty("items").EnumerateArray().First(r => r.GetProperty("entityId").GetString() == roleId.ToString());
        }

        async Task<int> SearchAsync(HttpClient reader, string term) =>
            (await (await reader.GetAsync($"/api/platform/audit?search={Uri.EscapeDataString(term)}&pageSize=50")).ReadJsonAsync()).GetProperty("totalCount").GetInt32();

        void AssertReduced(JsonElement row, string code)
        {
            Assert.True(row.GetProperty("detailsReduced").GetBoolean());
            var details = row.GetProperty("detailsJson").GetString()!;
            Assert.DoesNotContain(code, details, StringComparison.Ordinal);
            Assert.Contains("\"created\":true", details, StringComparison.Ordinal); // what happened is still told
            Assert.Contains("\"withheld\":[\"code\"]", details, StringComparison.Ordinal);
        }

        void AssertFull(JsonElement row, string code)
        {
            Assert.False(row.TryGetProperty("detailsReduced", out var reduced) && reduced.GetBoolean());
            Assert.Contains(code, row.GetProperty("detailsJson").GetString()!, StringComparison.Ordinal);
        }

        // No grant: both organizations' rows are reduced for both administrators, and the withheld text cannot be searched for.
        AssertReduced(await RowAsync(platform, _db.Data.OrganizationAId, roleA), codeA);
        AssertReduced(await RowAsync(platform, _db.Data.OrganizationBId, roleB), codeB);
        AssertReduced(await RowAsync(second, _db.Data.OrganizationAId, roleA), codeA);
        Assert.Equal(0, await SearchAsync(platform, codeA));
        Assert.Equal(0, await SearchAsync(platform, codeB));
        Assert.True(await SearchAsync(platform, roleA.ToString()) >= 1); // an identifier is not withheld

        // A grant that is only proposed changes nothing; approved, it opens organization A's rows to its holder alone.
        var ticketId = await CreateTicketAsync(alice, "Please review our roles");
        var grantId = (await ProposeAsync(platform, ticketId, new object[] { new { operation = "ConfigureRole", targetReferences = new[] { roleA.ToString() } } })).GetProperty("grantId").GetGuid();
        AssertReduced(await RowAsync(platform, _db.Data.OrganizationAId, roleA), codeA);
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/support/access-grants/{grantId}/approve", new { })).StatusCode);

        AssertFull(await RowAsync(platform, _db.Data.OrganizationAId, roleA), codeA);
        Assert.True(await SearchAsync(platform, codeA) >= 1);
        AssertReduced(await RowAsync(platform, _db.Data.OrganizationBId, roleB), codeB); // the grant is for A only
        Assert.Equal(0, await SearchAsync(platform, codeB));
        AssertReduced(await RowAsync(second, _db.Data.OrganizationAId, roleA), codeA);   // and for its holder only
        Assert.Equal(0, await SearchAsync(second, codeA));

        // Free text is withheld too: the proposal's reason, read by the administrator who holds no grant. The operation
        // code and the identifiers stay.
        var proposals = await (await second.GetAsync($"/api/platform/audit?organizationId={_db.Data.OrganizationAId}&eventType={Uri.EscapeDataString(AuditEvents.SupportAccessProposed)}&pageSize=200")).ReadJsonAsync();
        var proposal = proposals.GetProperty("items").EnumerateArray().First(r => r.GetProperty("entityId").GetString() == grantId.ToString());
        Assert.True(proposal.GetProperty("detailsReduced").GetBoolean());
        var proposalDetails = proposal.GetProperty("detailsJson").GetString()!;
        Assert.DoesNotContain("Requested in the ticket", proposalDetails, StringComparison.Ordinal);
        Assert.Contains("ConfigureRole", proposalDetails, StringComparison.Ordinal);
        Assert.Contains(roleA.ToString(), proposalDetails, StringComparison.Ordinal);

        // The grant ends: organization A's rows are reduced again.
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/support/access-grants/{grantId}/revoke", new { note = "Done." })).StatusCode);
        AssertReduced(await RowAsync(platform, _db.Data.OrganizationAId, roleA), codeA);
        Assert.Equal(0, await SearchAsync(platform, codeA));

        // Rows that belong to no organization (the platform's own) are never reduced.
        var platformRows = await (await platform.GetAsync($"/api/platform/audit?eventType={Uri.EscapeDataString(AuditEvents.LoginSucceeded)}&actorType=PlatformAdministrator&pageSize=5")).ReadJsonAsync();
        Assert.All(platformRows.GetProperty("items").EnumerateArray(), r => Assert.False(r.TryGetProperty("detailsReduced", out var reduced) && reduced.GetBoolean()));
    }

    [Fact]
    public async Task ExpiredGrants_EndTheSession_AndDenyExecution()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        await SetSecondApprovalAsync(alice, false);
        var ticketId = await CreateTicketAsync(alice, "Expiry ticket");
        using var platform = await LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        var grantId = (await ProposeAsync(platform, ticketId, new object[] { new { operation = "CreateOrganizationUser", targetReferences = new[] { "email:expired@org-a.test" } } })).GetProperty("grantId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/support/access-grants/{grantId}/approve", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await platform.PostJsonAsync($"/api/platform/support/access-grants/{grantId}/enter", new { })).StatusCode);

        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            var grant = await context.SupportAccessGrants.IgnoreQueryFilters().SingleAsync(g => g.GrantId == grantId);
            grant.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await context.SaveChangesAsync();
        }

        var denied = await ExecuteAsync(platform, "CreateOrganizationUser", null, new { email = "expired@org-a.test", fullName = "Expired" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var state = await (await platform.GetAsync($"/api/platform/support/access-grants/{grantId}")).ReadJsonAsync();
        Assert.Equal("Expired", state.GetProperty("status").GetString());
        Assert.False(state.GetProperty("sessionActive").GetBoolean());
        var timeline = (await (await alice.GetAsync($"/api/support/tickets/{ticketId}/events")).ReadJsonAsync()).EnumerateArray().Select(e => e.GetProperty("eventType").GetString()).ToList();
        Assert.Contains("SupportAccessExpired", timeline);
        Assert.Contains("SupportSessionEnded", timeline);

        // Only one open grant per ticket; a declined proposal frees the ticket again.
        var again = (await ProposeAsync(platform, ticketId, new object[] { new { operation = "CreateOrganizationUser", targetReferences = new[] { "email:expired@org-a.test" } } })).GetProperty("grantId").GetGuid();
        var duplicate = await platform.PostJsonAsync($"/api/platform/support/tickets/{ticketId}/access-grants", new { operations = new[] { new { operation = "CreateOrganizationUser", targetReferences = new[] { "email:expired@org-a.test" } } }, reason = "dup" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var decline = await alice.PostJsonAsync($"/api/support/access-grants/{again}/decline", new { note = "Not needed." });
        Assert.Equal("Declined", (await decline.ReadJsonAsync()).GetProperty("status").GetString());
        var platformNotifications = await (await platform.GetAsync("/api/notifications?unreadOnly=true")).ReadJsonAsync();
        Assert.Contains(platformNotifications.GetProperty("items").EnumerateArray(), n => n.GetProperty("notificationType").GetString() == "SupportAccess.Declined");
    }
}
