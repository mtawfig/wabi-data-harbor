using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WabiInsight.Api.Authentication;
using WabiInsight.Core.Storage;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Security;

/// <summary>P13.25: request limits per signed-in user, a real idle sign-out, lockout and open sessions, logo files.</summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class SignInHardeningTests
{
    private readonly SqlServerTestDatabase _db;

    public SignInHardeningTests(SqlServerTestDatabase db)
    {
        _db = db;
    }

    [Fact]
    public async Task TwoUsersBehindOneAddress_EachHaveTheirOwnRequestBudget()
    {
        // Both test clients come from the same address, as an office behind one router does.
        using var factory = new ApiFactory(_db.ConnectionString, new Dictionary<string, string?> { ["Security:ApiRateLimitPerMinute"] = "12" });
        using var alice = factory.CreateClient();
        using var bob = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await alice.LoginAsync(TestData.AliceEmail, TestData.UserPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.LoginAsync(TestData.BobEmail, TestData.UserPassword)).StatusCode);

        var last = HttpStatusCode.OK;
        for (var i = 0; i < 20 && last != HttpStatusCode.TooManyRequests; i++)
        {
            last = (await alice.GetAsync("/api/auth/me")).StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
        // Alice used up her minute; Bob, on the same address, is not affected by it.
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task BackgroundPolling_DoesNotKeepAnIdleSessionAlive()
    {
        var time = new ShiftedTimeProvider();
        using var host = new ApiFactory(_db.ConnectionString);
        using var factory = host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options => options.TimeProvider = time)));

        // An unattended tab: only the shell's timer talks to the API.
        using var idle = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await idle.LoginAsync(TestData.AliceEmail, TestData.UserPassword)).StatusCode);
        time.Advance(TimeSpan.FromMinutes(20));
        var poll = await idle.GetAsync("/api/notifications/unread-count");
        Assert.Equal(HttpStatusCode.OK, poll.StatusCode);
        Assert.DoesNotContain(SetCookies(poll), c => c.StartsWith(CookieAuthenticationSetup.CookieName + "=", StringComparison.Ordinal));
        time.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(HttpStatusCode.Unauthorized, (await idle.GetAsync("/api/notifications/unread-count")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await idle.GetAsync("/api/auth/me")).StatusCode);

        // Someone at work: their own request 20 minutes in renews the session, so it is still open 11 minutes later.
        using var active = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await active.LoginAsync(TestData.AliceEmail, TestData.UserPassword)).StatusCode);
        time.Advance(TimeSpan.FromMinutes(20));
        var own = await active.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Contains(SetCookies(own), c => c.StartsWith(CookieAuthenticationSetup.CookieName + "=", StringComparison.Ordinal));
        time.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(HttpStatusCode.OK, (await active.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Lockout_RefusesNewSignIns_ButDoesNotEndAnOpenSession()
    {
        await ClearLockoutAsync();
        try
        {
            using var factory = new ApiFactory(_db.ConnectionString);
            using var owner = factory.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await owner.LoginAsync(TestData.LockoutEmail, TestData.UserPassword)).StatusCode);

            // Someone else who only knows the address guesses five passwords.
            using var stranger = factory.CreateClient();
            for (var i = 0; i < 5; i++)
            {
                await stranger.LoginAsync(TestData.LockoutEmail, "wrong-password-123456");
            }

            var refused = await stranger.LoginAsync(TestData.LockoutEmail, TestData.UserPassword);
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
            Assert.EndsWith("/locked_out", (await refused.ReadJsonAsync()).GetProperty("type").GetString(), StringComparison.Ordinal);

            // The owner's session is re-checked against the database on this request (the tests validate every time).
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/auth/me")).StatusCode);
        }
        finally
        {
            await ClearLockoutAsync();
        }
    }

    [Fact]
    public async Task Logo_AcceptsPngAndJpegOnly_AndIsAnsweredUnderASandboxPolicy()
    {
        using var factory = new ApiFactory(_db.ConnectionString);
        using var alice = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await alice.LoginAsync(TestData.AliceEmail, TestData.UserPassword)).StatusCode);

        const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\" onclick=\"alert(1)\"/></svg>";
        using (var svg = new MultipartFormDataContent { { new StringContent(Svg) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/svg+xml") } }, "file", "logo.svg" } })
        {
            var refused = await alice.PutAsync(new Uri("/api/organization/logo", UriKind.Relative), svg);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("logo_type_invalid", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        // The same bytes declared as PNG are refused too: the file has to be what it says.
        using (var disguised = new MultipartFormDataContent { { new StringContent(Svg) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png") } }, "file", "logo.png" } })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await alice.PutAsync(new Uri("/api/organization/logo", UriKind.Relative), disguised)).StatusCode);
        }

        string? previousKey;
        string? previousType;
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            var organization = await context.Organizations.SingleAsync(o => o.OrganizationId == _db.Data.OrganizationAId);
            (previousKey, previousType) = (organization.LogoArtifactKey, organization.LogoContentType);
        }

        try
        {
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
            using (var form = new MultipartFormDataContent { { new ByteArrayContent(png) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png") } }, "file", "logo.png" } })
            {
                Assert.Equal(HttpStatusCode.NoContent, (await alice.PutAsync(new Uri("/api/organization/logo", UriKind.Relative), form)).StatusCode);
            }

            var raster = await alice.GetAsync("/api/organization/logo");
            Assert.Equal(HttpStatusCode.OK, raster.StatusCode);
            Assert.Contains("sandbox", Assert.Single(raster.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
            Assert.Null(raster.Content.Headers.ContentDisposition);

            // A logo stored as SVG before this rule: still answered, but as a download that cannot run in the site's origin.
            using (var scope = factory.Services.CreateScope())
            {
                var storage = scope.ServiceProvider.GetRequiredService<IArtifactStorage>();
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Svg));
                var key = await storage.SaveAsync("organization-logos", stream);
                await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
                var organization = await context.Organizations.SingleAsync(o => o.OrganizationId == _db.Data.OrganizationAId);
                organization.LogoArtifactKey = key;
                organization.LogoContentType = "image/svg+xml";
                await context.SaveChangesAsync();
            }

            var legacy = await alice.GetAsync("/api/organization/logo");
            Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
            Assert.Equal("image/svg+xml", legacy.Content.Headers.ContentType!.MediaType);
            var policy = Assert.Single(legacy.Headers.GetValues("Content-Security-Policy"));
            Assert.Contains("default-src 'none'", policy, StringComparison.Ordinal);
            Assert.Contains("sandbox", policy, StringComparison.Ordinal);
            Assert.Equal("attachment", legacy.Content.Headers.ContentDisposition!.DispositionType);
            Assert.Equal("nosniff", Assert.Single(legacy.Headers.GetValues("X-Content-Type-Options")));
        }
        finally
        {
            await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
            var organization = await context.Organizations.SingleAsync(o => o.OrganizationId == _db.Data.OrganizationAId);
            organization.LogoArtifactKey = previousKey;
            organization.LogoContentType = previousType;
            await context.SaveChangesAsync();
        }
    }

    private static IEnumerable<string> SetCookies(HttpResponseMessage response)
        => response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];

    private async Task ClearLockoutAsync()
    {
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var user = await context.OrganizationUsers.IgnoreQueryFilters().SingleAsync(u => u.UserId == _db.Data.UserALockoutId);
        user.LockoutEnd = null;
        user.FailedLoginCount = 0;
        await context.SaveChangesAsync();
    }

    /// <summary>The cookie handler's clock, moved forward by the test instead of waiting half an hour.</summary>
    private sealed class ShiftedTimeProvider : TimeProvider
    {
        private TimeSpan _offset = TimeSpan.Zero;

        public void Advance(TimeSpan by) => _offset += by;

        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + _offset;
    }
}
