using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Common;
using WabiInsight.Core.Email;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Authentication;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Auth;

/// <summary>
/// P13.32: a forgotten password is reset through a one-time link sent by e-mail, by organization users and Platform
/// Administrators alike, without telling anyone which addresses have accounts.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed partial class PasswordResetTests : IDisposable
{
    private const string NewPassword = "Reset#Strong#Pass#2026";

    private readonly SqlServerTestDatabase _db;
    private readonly ApiFactory _factory;

    public PasswordResetTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new ApiFactory(db.ConnectionString);
    }

    public void Dispose() => _factory.Dispose();

    [GeneratedRegex("/reset-password#token=([A-Za-z0-9_-]+)")]
    private static partial Regex LinkToken();

    private static string TokenOf(EmailMessage message)
    {
        var match = LinkToken().Match(message.TextBody);
        Assert.True(match.Success, "The e-mail carries no reset link.");
        return match.Groups[1].Value;
    }

    private static bool IsLink(EmailMessage message) => message.TextBody.Contains("/reset-password#token=", StringComparison.Ordinal);

    private async Task<(Guid UserId, string Email)> InsertUserAsync(Guid organizationId, string language = "en")
    {
        var email = $"p1332.{Guid.NewGuid():N}@org.test";
        var userId = Ids.New();
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var user = TestData.NewUser(userId, organizationId, email, "Reset User");
        user.PreferredLanguage = language;
        context.OrganizationUsers.Add(user);
        context.LoginIdentities.Add(TestData.Identity(email, PrincipalType.OrganizationUser, userId, organizationId, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
        return (userId, email);
    }

    private static async Task<HttpClient> AnonymousAsync(WebApplicationFactory<Program> api)
    {
        var client = api.CreateClient();
        await client.RefreshCsrfAsync();
        return client;
    }

    private static Task<HttpResponseMessage> ForgotAsync(HttpClient client, string email) => client.PostJsonAsync("/api/auth/forgot-password", new { email });

    private static async Task<bool> IsValidAsync(HttpClient client, string token)
        => (await (await client.PostJsonAsync("/api/auth/reset-password/check", new { token })).ReadJsonAsync()).GetProperty("valid").GetBoolean();

    private static Task<HttpResponseMessage> ResetAsync(HttpClient client, string token, string newPassword) => client.PostJsonAsync("/api/auth/reset-password", new { token, newPassword });

    [Fact]
    public async Task OrganizationUser_ResetsAForgottenPassword_ThroughTheLinkInTheEmail()
    {
        var (userId, email) = await InsertUserAsync(_db.Data.OrganizationAId);
        using var openSession = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await openSession.LoginAsync(email, TestData.UserPassword)).StatusCode);

        using var visitor = await AnonymousAsync(_factory);
        var asked = await ForgotAsync(visitor, email);
        Assert.Equal(HttpStatusCode.NoContent, asked.StatusCode);
        Assert.Empty(await asked.Content.ReadAsStringAsync());

        // The e-mail has a link and no password; the database has the hash of the token, never the token.
        var mail = await _factory.Emails.WaitForAsync(email, IsLink);
        var token = TokenOf(mail);
        Assert.Equal(43, token.Length);
        Assert.Contains("30 minutes", mail.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Temporary password", mail.TextBody, StringComparison.Ordinal);
        Assert.Contains("Choose a new password", mail.HtmlBody, StringComparison.Ordinal);
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            var request = await context.PasswordResetRequests.AsNoTracking().SingleAsync(r => r.PrincipalId == userId);
            Assert.Equal(PasswordResetService.Hash(token), request.TokenHash);
            Assert.NotEqual(token, request.TokenHash);
            Assert.Equal(_db.Data.OrganizationAId, request.OrganizationId);
            Assert.InRange(request.ExpiresAt - request.CreatedAt, TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(31));
            var row = await context.EmailMessages.IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.RecipientEmail == email);
            Assert.Equal(EmailTemplateCodes.PasswordResetLink, row.TemplateCode);
            Assert.Equal(_db.Data.OrganizationAId, row.OrganizationId);
        }

        // Asking changes nothing by itself: the current password and the open session still work.
        using var stillWorks = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await stillWorks.LoginAsync(email, TestData.UserPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await openSession.GetAsync("/api/auth/me")).StatusCode);

        // A password that breaks the rules is refused and does not spend the link.
        Assert.True(await IsValidAsync(visitor, token));
        var weak = await ResetAsync(visitor, token, "short");
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("password_too_short", await weak.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.True(await IsValidAsync(visitor, token));

        var done = await ResetAsync(visitor, token, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, done.StatusCode);

        // The link works once.
        Assert.False(await IsValidAsync(visitor, token));
        var again = await ResetAsync(visitor, token, "Another#Strong#Pass#2026");
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Contains("reset_link_invalid", await again.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Nobody was signed in by it; the old password and every session that was open are over; the new password works.
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await openSession.GetAsync("/api/auth/me")).StatusCode);
        using var afterwards = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await afterwards.LoginAsync(email, TestData.UserPassword)).StatusCode);
        var signedIn = await afterwards.LoginAsync(email, NewPassword);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.False((await signedIn.ReadJsonAsync()).GetProperty("mustChangePassword").GetBoolean());

        // The owner is told, and both steps are in the organization's audit trail. Organization B sees and loses nothing.
        var notice = await _factory.Emails.WaitForAsync(email, m => m.Subject.Contains("password was changed", StringComparison.Ordinal));
        Assert.Contains("organization administrator", notice.TextBody, StringComparison.Ordinal);
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            var events = await context.AuditLogs.IgnoreQueryFilters().AsNoTracking().Where(a => a.EntityId == userId.ToString() && a.EventType.StartsWith("Auth.PasswordReset")).ToListAsync();
            Assert.Equal(["Auth.PasswordResetCompleted", "Auth.PasswordResetRequested"], events.Select(e => e.EventType).Order().ToArray());
            Assert.All(events, e => Assert.Equal(_db.Data.OrganizationAId, e.OrganizationId));
            Assert.DoesNotContain(events, e => (e.DetailsJson ?? string.Empty).Contains(token, StringComparison.Ordinal));
            Assert.False(await context.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.EntityId == userId.ToString() && a.OrganizationId != _db.Data.OrganizationAId));
        }

        using var bob = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await bob.LoginAsync(TestData.BobEmail, TestData.UserPassword)).StatusCode);
    }

    [Fact]
    public async Task EveryAddress_GetsTheSameAnswer_AndOnlyAnAccountThatMaySignInGetsALink()
    {
        using var visitor = await AnonymousAsync(_factory);
        var unknown = $"nobody.{Guid.NewGuid():N}@org.test";
        foreach (var address in new[] { unknown, TestData.InactiveEmail, TestData.SuspendedOrgUserEmail, "not an address" })
        {
            var answer = await ForgotAsync(visitor, address);
            Assert.Equal(HttpStatusCode.NoContent, answer.StatusCode);
            Assert.Empty(await answer.Content.ReadAsStringAsync());
        }

        // A real account answers exactly the same; its link is the proof that requests are being served at all.
        var (_, email) = await InsertUserAsync(_db.Data.OrganizationBId);
        var real = await ForgotAsync(visitor, email);
        Assert.Equal(HttpStatusCode.NoContent, real.StatusCode);
        Assert.Empty(await real.Content.ReadAsStringAsync());
        await _factory.Emails.WaitForAsync(email, IsLink);

        Assert.DoesNotContain(_factory.Emails.Messages, m => m.ToEmail is TestData.InactiveEmail or TestData.SuspendedOrgUserEmail || m.ToEmail == unknown);
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        Assert.False(await context.PasswordResetRequests.AnyAsync(r => r.PrincipalId == _db.Data.UserAInactiveId || r.PrincipalId == _db.Data.UserCSuspendedId));
        var ignored = await context.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.EventType == "Auth.PasswordResetIgnored" && (a.EntityId == _db.Data.UserAInactiveId.ToString() || a.EntityId == _db.Data.UserCSuspendedId.ToString()))
            .Select(a => a.DetailsJson!).ToListAsync();
        Assert.Contains(ignored, d => d.Contains("inactive", StringComparison.Ordinal));
        Assert.Contains(ignored, d => d.Contains("organization_suspended", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnlyTheNewestLinkWorks_AnAccountGetsThreeAnHour_AndTheEmailFollowsTheAccountsLanguage()
    {
        var (userId, email) = await InsertUserAsync(_db.Data.OrganizationAId, language: "ar");
        using var visitor = await AnonymousAsync(_factory);

        Assert.Equal(HttpStatusCode.NoContent, (await ForgotAsync(visitor, email)).StatusCode);
        var first = await _factory.Emails.WaitForAsync(email, IsLink);
        Assert.Equal("ar", first.Language);
        Assert.Contains("dir=\"rtl\"", first.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("اختيار كلمة مرور جديدة", first.HtmlBody, StringComparison.Ordinal);
        var firstToken = TokenOf(first);

        Assert.Equal(HttpStatusCode.NoContent, (await ForgotAsync(visitor, email.ToUpperInvariant())).StatusCode);
        var second = await _factory.Emails.WaitForAsync(email, m => IsLink(m) && TokenOf(m) != firstToken);
        var secondToken = TokenOf(second);
        Assert.False(await IsValidAsync(visitor, firstToken));
        Assert.True(await IsValidAsync(visitor, secondToken));

        // The third is sent; the fourth within the hour is ignored with the same answer.
        Assert.Equal(HttpStatusCode.NoContent, (await ForgotAsync(visitor, email)).StatusCode);
        var third = await _factory.Emails.WaitForAsync(email, m => IsLink(m) && TokenOf(m) != firstToken && TokenOf(m) != secondToken);
        Assert.Equal(HttpStatusCode.NoContent, (await ForgotAsync(visitor, email)).StatusCode);
        await Task.Delay(500);
        Assert.Equal(3, _factory.Emails.Messages.Count(m => m.ToEmail == email && IsLink(m)));
        Assert.True(await IsValidAsync(visitor, TokenOf(third)));

        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        Assert.Equal(3, await context.PasswordResetRequests.CountAsync(r => r.PrincipalId == userId));
        Assert.Equal(2, await context.PasswordResetRequests.CountAsync(r => r.PrincipalId == userId && r.CancelledAt != null));
        var ignored = await context.AuditLogs.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.EventType == "Auth.PasswordResetIgnored" && a.EntityId == userId.ToString());
        Assert.Contains("too_many_requests", ignored.DetailsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALinkExpires_AndEndsWhenThePasswordChangesAnotherWay()
    {
        var (userId, email) = await InsertUserAsync(_db.Data.OrganizationAId);
        using var visitor = await AnonymousAsync(_factory);

        await ForgotAsync(visitor, email);
        var expiring = TokenOf(await _factory.Emails.WaitForAsync(email, IsLink));
        Assert.True(await IsValidAsync(visitor, expiring));

        // Thirty-one minutes later (the request is aged in the database; its 30-minute lifetime is checked in the first test).
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            var request = await context.PasswordResetRequests.SingleAsync(r => r.PrincipalId == userId);
            request.CreatedAt = request.CreatedAt.AddMinutes(-31);
            request.ExpiresAt = request.ExpiresAt.AddMinutes(-31);
            await context.SaveChangesAsync();
        }

        Assert.False(await IsValidAsync(visitor, expiring));
        var late = await ResetAsync(visitor, expiring, NewPassword);
        Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
        Assert.Contains("reset_link_invalid", await late.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // A new link, then the person remembers the password and changes it while signed in: the link is over.
        await ForgotAsync(visitor, email);
        var second = TokenOf(await _factory.Emails.WaitForAsync(email, m => IsLink(m) && TokenOf(m) != expiring));
        Assert.True(await IsValidAsync(visitor, second));
        using var owner = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await owner.LoginAsync(email, TestData.UserPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostJsonAsync("/api/auth/change-password", new { currentPassword = TestData.UserPassword, newPassword = "Remembered#Strong#2026" })).StatusCode);
        Assert.False(await IsValidAsync(visitor, second));
        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync(visitor, second, NewPassword)).StatusCode);
        using var check = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await check.LoginAsync(email, "Remembered#Strong#2026")).StatusCode);
    }

    [Fact]
    public async Task PlatformAdministrator_ResetsAForgottenPassword_TheSameWay()
    {
        var email = $"p1332.admin.{Guid.NewGuid():N}@wabitex.test";
        var administratorId = Ids.New();
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            context.Administrators.Add(TestData.NewAdministrator(administratorId, email, "Locked Out Administrator", isBootstrap: false));
            context.LoginIdentities.Add(TestData.Identity(email, PrincipalType.PlatformAdministrator, administratorId, null, DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
        }

        using var visitor = await AnonymousAsync(_factory);
        Assert.Equal(HttpStatusCode.NoContent, (await ForgotAsync(visitor, email)).StatusCode);
        var token = TokenOf(await _factory.Emails.WaitForAsync(email, IsLink));
        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(visitor, token, NewPassword)).StatusCode);

        using var administrator = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await administrator.LoginAsync(email, TestData.AdminPassword)).StatusCode);
        var signedIn = await administrator.LoginAsync(email, NewPassword);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.Equal("PlatformAdministrator", (await signedIn.ReadJsonAsync()).GetProperty("principalType").GetString());
        Assert.Equal(HttpStatusCode.OK, (await administrator.GetAsync("/api/platform/health")).StatusCode);

        // An administrator's request belongs to no organization, and the notice names who can help.
        var notice = await _factory.Emails.WaitForAsync(email, m => m.Subject.Contains("password was changed", StringComparison.Ordinal));
        Assert.Contains("another Platform Administrator", notice.TextBody, StringComparison.Ordinal);
        await using var after = _db.CreateContext(_db.Data.PlatformAdministrator);
        var request = await after.PasswordResetRequests.AsNoTracking().SingleAsync(r => r.PrincipalId == administratorId);
        Assert.Null(request.OrganizationId);
        Assert.NotNull(request.UsedAt);
        Assert.All(await after.EmailMessages.IgnoreQueryFilters().AsNoTracking().Where(m => m.RecipientEmail == email).ToListAsync(), m => Assert.Null(m.OrganizationId));
    }

    [Fact]
    public async Task ResetEndpoints_AreRateLimitedPerAddress_AndNeedTheAntiforgeryToken()
    {
        using var limited = new ApiFactory(_db.ConnectionString, new Dictionary<string, string?> { ["Security:PasswordResetRateLimitPerMinute"] = "2" });
        using var visitor = await AnonymousAsync(limited);
        Assert.Equal(HttpStatusCode.OK, (await visitor.PostJsonAsync("/api/auth/reset-password/check", new { token = new string('a', 43) })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ForgotAsync(visitor, "someone@org.test")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await ForgotAsync(visitor, "someone@org.test")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await ResetAsync(visitor, new string('a', 43), NewPassword)).StatusCode);

        using var noToken = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await ForgotAsync(noToken, "someone@org.test")).StatusCode);
    }
}
