using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Security;

/// <summary>P12.1 hardening: response headers, request-body limits, rate limits, authorization coverage of every controller, error responses.</summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class HardeningTests : IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly ApiFactory _factory;

    public HardeningTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new ApiFactory(db.ConnectionString);
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task EveryResponse_CarriesDefensiveHeaders_AndIsNeverCached()
    {
        using var client = _factory.CreateClient();
        var anonymous = await client.GetAsync("/api/platform/info");
        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
        AssertHeaders(anonymous);
        Assert.Contains("default-src 'none'", anonymous.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);

        var unauthorized = await client.GetAsync("/api/history");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        AssertHeaders(unauthorized);

        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(TestData.AliceEmail, TestData.UserPassword)).StatusCode);
        var me = await client.GetAsync("/api/auth/me");
        AssertHeaders(me);
        Assert.True(me.Headers.CacheControl!.NoStore);
        Assert.False(me.Headers.Contains("Server"));

        static void AssertHeaders(HttpResponseMessage response)
        {
            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
            Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
            Assert.Equal("same-origin", response.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
            Assert.Equal("same-origin", response.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
            Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
            Assert.Contains("camera=()", response.Headers.GetValues("Permissions-Policy").Single(), StringComparison.Ordinal);
            Assert.True(response.Headers.CacheControl?.NoStore == true, "Cache-Control must carry no-store");
        }
    }

    [Fact]
    public async Task OversizedJsonBody_IsRefusedBeforeReading_WhileUploadsKeepTheirOwnLimit()
    {
        using var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(TestData.AliceEmail, TestData.UserPassword)).StatusCode);
        var big = new string('x', 1_200_000);
        var refused = await client.PostJsonAsync("/api/support/tickets", new { ticketType = "Request", impact = "Low", subject = "big", description = big });
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, refused.StatusCode);
        Assert.Equal("request_body_too_large", (await refused.ReadJsonAsync()).GetProperty("detail").GetString());

        // The logo endpoint declares a 2 MB limit of its own: a 1.5 MB body reaches validation (and is rejected as an invalid image, not as too large).
        using var form = new MultipartFormDataContent { { new ByteArrayContent(new byte[1_500_000]) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png") } }, "file", "logo.png" } };
        var logo = await client.PutAsync(new Uri("/api/organization/logo", UriKind.Relative), form);
        Assert.True(logo.StatusCode == HttpStatusCode.BadRequest, $"{(int)logo.StatusCode}: {await logo.Content.ReadAsStringAsync()}");

        // Within the default limit everything works as before.
        var ok = await client.PostJsonAsync("/api/support/tickets", new { ticketType = "Request", impact = "Low", subject = "Within limits", description = new string('y', 5_000), requiresSupportAccess = false });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task AnalysisEndpoints_AreRateLimitedPerUser()
    {
        using var factory = new ApiFactory(_db.ConnectionString, new Dictionary<string, string?> { ["Security:AiRateLimitPerMinute"] = "1" });
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(TestData.AliceEmail, TestData.UserPassword)).StatusCode);
        var first = await client.PostJsonAsync("/api/ai/plan", new { });
        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
        var second = await client.PostJsonAsync("/api/ai/plan", new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        // Other endpoints are untouched by the analysis budget.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task WholeApi_HasAGlobalBudgetPerPrincipal()
    {
        using var factory = new ApiFactory(_db.ConnectionString, new Dictionary<string, string?> { ["Security:ApiRateLimitPerMinute"] = "4" });
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(TestData.AliceEmail, TestData.UserPassword)).StatusCode);
        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 6; i++)
        {
            last = (await client.GetAsync("/api/auth/me")).StatusCode;
            if (last == HttpStatusCode.TooManyRequests)
            {
                break;
            }
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public void EveryController_RequiresAuthorization_ExceptTheKnownPublicOnes()
    {
        var assembly = typeof(Program).Assembly;
        var controllers = assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract).ToList();
        Assert.True(controllers.Count > 20, "controller discovery failed");
        string[] publicActions = ["AuthController.Csrf", "AuthController.Login", "BootstrapController.Status", "BootstrapController.Initialize", "PlatformInfoController.Get"];
        var seen = new List<string>();
        foreach (var controller in controllers)
        {
            var controllerAuthorize = controller.GetCustomAttribute<AuthorizeAttribute>(inherit: true);
            var controllerAnonymous = controller.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null;
            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any()))
            {
                var name = $"{controller.Name}.{action.Name}";
                var anonymous = controllerAnonymous || action.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
                if (anonymous)
                {
                    Assert.Contains(name, publicActions);
                    seen.Add(name);
                    continue;
                }

                var authorize = action.GetCustomAttribute<AuthorizeAttribute>() ?? controllerAuthorize;
                if (authorize is null)
                {
                    // Session-only actions on the public auth controller rely on the fallback policy (authenticated user), verified below.
                    Assert.Equal("AuthController", controller.Name);
                    continue;
                }

                // A bare [Authorize] means "any signed-in principal" (notifications serve both account types); every other controller names its policy.
                Assert.True(!string.IsNullOrEmpty(authorize.Policy) || controller.Name == "NotificationsController", $"{name} is authorized without a named policy.");
            }
        }

        Assert.Equal(publicActions.OrderBy(a => a, StringComparer.Ordinal), seen.OrderBy(a => a, StringComparer.Ordinal));
        var fallback = _factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthorizationOptions>>().Value.FallbackPolicy;
        Assert.NotNull(fallback);
        Assert.Contains(fallback.Requirements, r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public void UploadEndpoints_DeclareTheirOwnSizeLimit()
    {
        var endpoints = _factory.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints.OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>().ToList();
        var logo = endpoints.Single(e => e.RoutePattern.RawText == "api/organization/logo" && e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!.HttpMethods.Contains("PUT"));
        Assert.True(WabiInsight.Api.Security.RequestBodyLimitMiddleware.HasOwnPolicy(logo));
        var attachments = endpoints.Single(e => e.RoutePattern.RawText == "api/support/tickets/{id:guid}/attachments" && e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!.HttpMethods.Contains("POST"));
        Assert.True(WabiInsight.Api.Security.RequestBodyLimitMiddleware.HasOwnPolicy(attachments));
        var plain = endpoints.Single(e => e.RoutePattern.RawText == "api/support/tickets" && e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!.HttpMethods.Contains("POST"));
        Assert.False(WabiInsight.Api.Security.RequestBodyLimitMiddleware.HasOwnPolicy(plain));
    }

    [Fact]
    public async Task Errors_NeverLeakInternals()
    {
        using var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(TestData.AliceEmail, TestData.UserPassword)).StatusCode);
        var notFound = await client.GetAsync($"/api/history/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        var malformed = await client.PostAsync(new Uri("/api/support/tickets", UriKind.Relative), new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        var body = await malformed.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        var unknownRoute = await client.GetAsync("/api/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, unknownRoute.StatusCode);
    }
}
