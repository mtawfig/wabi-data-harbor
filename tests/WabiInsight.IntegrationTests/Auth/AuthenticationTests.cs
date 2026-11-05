using System.Net;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;
using WabiInsight.IntegrationTests.Email;

namespace WabiInsight.IntegrationTests.Auth;

[Collection(DatabaseCollectionDefinition.Name)]
public sealed class AuthenticationTests : IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly ApiFactory _factory;

    public AuthenticationTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new ApiFactory(db.ConnectionString);
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task PlatformAdministrator_LogsIn_WithoutOrganization()
    {
        using var client = _factory.CreateClient();

        var response = await client.LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        var session = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("PlatformAdministrator", session.GetProperty("principalType").GetString());
        Assert.Equal(_db.Data.AdministratorId, session.GetProperty("id").GetGuid());
        Assert.False(session.TryGetProperty("organizationId", out _));
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.Contains(cookies, c => c.StartsWith("wi.session=", StringComparison.Ordinal) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OrganizationUser_LogsIn_WithTrustedOrganization()
    {
        using var client = _factory.CreateClient();

        var response = await client.LoginAsync(TestData.AliceEmail, TestData.UserPassword);
        var session = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OrganizationUser", session.GetProperty("principalType").GetString());
        Assert.Equal(_db.Data.OrganizationAId, session.GetProperty("organizationId").GetGuid());

        var me = await (await client.GetAsync("/api/auth/me")).ReadJsonAsync();
        Assert.Equal(_db.Data.OrganizationAId, me.GetProperty("organizationId").GetGuid());
    }

    [Fact]
    public async Task UnknownEmail_AndWrongPassword_ProduceIdenticalResponses()
    {
        using var client = _factory.CreateClient();

        var unknown = await client.LoginAsync("nobody@nowhere.test", "whatever-password-123");
        var wrong = await client.LoginAsync(TestData.BobEmail, "wrong-password-123456");

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        var unknownBody = await unknown.ReadJsonAsync();
        var wrongBody = await wrong.ReadJsonAsync();
        Assert.Equal(unknownBody.GetProperty("type").GetString(), wrongBody.GetProperty("type").GetString());
        Assert.Equal(unknownBody.GetProperty("detail").GetString(), wrongBody.GetProperty("detail").GetString());
        Assert.EndsWith("/invalid_credentials", unknownBody.GetProperty("type").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FiveFailures_LockTheAccount()
    {
        using var client = _factory.CreateClient();

        for (var i = 0; i < 4; i++)
        {
            var attempt = await client.LoginAsync(TestData.LockoutEmail, "wrong-password-123456");
            Assert.EndsWith("/invalid_credentials", (await attempt.ReadJsonAsync()).GetProperty("type").GetString(), StringComparison.Ordinal);
        }

        var fifth = await client.LoginAsync(TestData.LockoutEmail, "wrong-password-123456");
        Assert.EndsWith("/locked_out", (await fifth.ReadJsonAsync()).GetProperty("type").GetString(), StringComparison.Ordinal);

        var correct = await client.LoginAsync(TestData.LockoutEmail, TestData.UserPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);
        Assert.EndsWith("/locked_out", (await correct.ReadJsonAsync()).GetProperty("type").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InactiveUser_CannotLogIn()
    {
        using var client = _factory.CreateClient();

        var response = await client.LoginAsync(TestData.InactiveEmail, TestData.UserPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.EndsWith("/account_disabled", (await response.ReadJsonAsync()).GetProperty("type").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UserOfSuspendedOrganization_CannotLogIn()
    {
        using var client = _factory.CreateClient();

        var response = await client.LoginAsync(TestData.SuspendedOrgUserEmail, TestData.UserPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.EndsWith("/organization_suspended", (await response.ReadJsonAsync()).GetProperty("type").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProtectedEndpoints_RequireAuthentication_ByDefault()
    {
        using var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/platform/info")).StatusCode);
    }

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        using var client = _factory.CreateClient();
        await client.LoginAsync(TestData.BobEmail, TestData.UserPassword);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        var logout = await client.PostJsonAsync("/api/auth/logout", new { });

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task OrganizationUser_IsDeniedPlatformEndpoints()
    {
        using var client = _factory.CreateClient();
        await client.LoginAsync(TestData.AliceEmail, TestData.UserPassword);

        var response = await client.PostJsonAsync($"/api/platform/administrators/{_db.Data.SecondAdministratorId}/reset-password", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnsafeRequest_WithoutAntiforgeryHeader_IsRejected()
    {
        using var client = _factory.CreateClient();
        await client.RefreshCsrfAsync();
        client.DefaultRequestHeaders.Remove(ApiClientExtensions.CsrfHeader);

        var response = await client.PostJsonAsync("/api/auth/login", new { email = TestData.BobEmail, password = TestData.UserPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_RejectsWrongCurrent_AndWeakNew()
    {
        using var client = _factory.CreateClient();
        await client.LoginAsync(TestData.AliceEmail, TestData.UserPassword);

        var wrongCurrent = await client.PostJsonAsync("/api/auth/change-password", new { currentPassword = "not-the-password-1", newPassword = "Another#Strong#Pass#2026" });
        var tooShort = await client.PostJsonAsync("/api/auth/change-password", new { currentPassword = TestData.UserPassword, newPassword = "short" });
        var sample = await client.PostJsonAsync("/api/auth/change-password", new { currentPassword = TestData.UserPassword, newPassword = "my_default_password_change_it" });

        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Contains("password_too_short", await tooShort.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("password_is_sample", await sample.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangePassword_InvalidatesOtherSessions_AndKeepsCurrentOne()
    {
        using var first = _factory.CreateClient();
        using var second = _factory.CreateClient();
        await first.LoginAsync(TestData.ChangerEmail, TestData.UserPassword);
        await second.LoginAsync(TestData.ChangerEmail, TestData.UserPassword);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/auth/me")).StatusCode);

        const string newPassword = "Changed#Strong#Pass#2026";
        var change = await first.PostJsonAsync("/api/auth/change-password", new { currentPassword = TestData.UserPassword, newPassword });

        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/auth/me")).StatusCode);

        using var third = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await third.LoginAsync(TestData.ChangerEmail, TestData.UserPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await third.LoginAsync(TestData.ChangerEmail, newPassword)).StatusCode);
    }

    [Fact]
    public async Task Administrator_CanResetAnotherAdministrator_TemporaryPasswordIsEmailedNotReturned()
    {
        using var admin = _factory.CreateClient();
        await admin.LoginAsync(TestData.AdminEmail, TestData.AdminPassword);

        var self = await admin.PostJsonAsync($"/api/platform/administrators/{_db.Data.AdministratorId}/reset-password", new { });
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);

        var reset = await admin.PostJsonAsync($"/api/platform/administrators/{_db.Data.SecondAdministratorId}/reset-password", new { });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var body = await reset.Content.ReadAsStringAsync();
        Assert.DoesNotContain("temporaryPassword", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("s***d@wabitex.test", (await reset.ReadJsonAsync()).GetProperty("deliveredTo").GetString());

        var email = await _factory.Emails.WaitForAsync(TestData.SecondAdminEmail);
        var temporary = RecordingEmailSender.TemporaryPasswordOf(email);
        Assert.DoesNotContain(temporary, body, StringComparison.Ordinal);

        using var second = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.LoginAsync(TestData.SecondAdminEmail, TestData.AdminPassword)).StatusCode);
        var login = await second.LoginAsync(TestData.SecondAdminEmail, temporary);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await login.ReadJsonAsync()).GetProperty("mustChangePassword").GetBoolean());

        var blocked = await second.PostJsonAsync($"/api/platform/administrators/{Guid.NewGuid()}/reset-password", new { });
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Contains("password-change-required", await blocked.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var change = await second.PostJsonAsync("/api/auth/change-password", new { currentPassword = temporary, newPassword = "Second#New#Strong#2026" });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.False((await change.ReadJsonAsync()).GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await second.PostJsonAsync($"/api/platform/administrators/{Guid.NewGuid()}/reset-password", new { })).StatusCode);
    }
}
