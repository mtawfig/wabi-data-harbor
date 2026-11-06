using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Auth;

/// <summary>
/// Behind nginx / IIS + ARR every connection comes from the proxy. The per-client login limit must follow
/// X-Forwarded-For from a trusted (loopback) proxy, and ignore it from anyone else.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class ForwardedHeadersTests
{
    private readonly SqlServerTestDatabase _db;

    public ForwardedHeadersTests(SqlServerTestDatabase db)
    {
        _db = db;
    }

    [Fact]
    public async Task LoginLimit_IsPerForwardedClient_WhenTheProxyIsLoopback()
    {
        using var factory = new ConnectionAddressFactory(_db.ConnectionString, IPAddress.Loopback);
        using var client = factory.CreateClient();
        await client.RefreshCsrfAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginFromAsync(client, "203.0.113.10")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginFromAsync(client, "203.0.113.10")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginFromAsync(client, "203.0.113.10")).StatusCode);

        // Another visitor behind the same proxy keeps their own budget.
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginFromAsync(client, "198.51.100.20")).StatusCode);
    }

    [Fact]
    public async Task ForwardedFor_IsIgnored_FromAnUntrustedAddress()
    {
        using var factory = new ConnectionAddressFactory(_db.ConnectionString, IPAddress.Parse("192.0.2.50"));
        using var client = factory.CreateClient();
        await client.RefreshCsrfAsync();

        // Rotating the header does not buy fresh attempts: the limit stays on the real connection address.
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginFromAsync(client, "203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginFromAsync(client, "203.0.113.2")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginFromAsync(client, "203.0.113.3")).StatusCode);
    }

    // An unknown e-mail: failed attempts must never lock out a shared test account.
    private static async Task<HttpResponseMessage> LoginFromAsync(HttpClient client, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "nobody@forwarded-headers.test", password = "wrong-password-123456" }),
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        request.Headers.Add("X-Forwarded-Proto", "https");
        return await client.SendAsync(request);
    }

    /// <summary>The in-memory test server has no socket; this sets the connection address the proxy check sees.</summary>
    private sealed class ConnectionAddressFactory : ApiFactory
    {
        private readonly IPAddress _connectionAddress;

        public ConnectionAddressFactory(string connectionString, IPAddress connectionAddress)
            : base(connectionString, new Dictionary<string, string?> { ["Security:LoginRateLimitPerMinute"] = "2" })
        {
            _connectionAddress = connectionAddress;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new ConnectionAddressStartupFilter(_connectionAddress)));
        }
    }

    private sealed class ConnectionAddressStartupFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
