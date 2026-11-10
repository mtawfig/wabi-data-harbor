using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WabiInsight.Api.Hosting;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Api;

/// <summary>
/// P13.18: with Web:RootPath the API also serves the Angular build (Azure App Service): the shell with its own security
/// policy, deep links answered with index.html, hashed files cached, and /api and /health untouched. Without the setting
/// nothing is served, as behind nginx or IIS.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class WebAppHostingTests : IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wi-web-root", Guid.NewGuid().ToString("N"));

    public WebAppHostingTests(SqlServerTestDatabase db)
    {
        _db = db;
        Directory.CreateDirectory(Path.Combine(_root, "i18n"));
        File.WriteAllText(Path.Combine(_root, "index.html"), "<!doctype html><html><body><wi-root></wi-root><script src=\"main-ABC123.js\"></script></body></html>");
        File.WriteAllText(Path.Combine(_root, "main-ABC123.js"), "console.log('wabi');");
        File.WriteAllText(Path.Combine(_root, "i18n", "en.json"), "{\"app\":\"Wabi Insight\"}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task TheApi_ServesTheWebApp_WhenWebRootPathIsSet()
    {
        using var factory = new ApiFactory(_db.ConnectionString, new Dictionary<string, string?> { [WebAppHosting.RootPathKey] = _root });
        using var client = factory.CreateClient();

        foreach (var page in new[] { "/", "/history/01a1c261-d60c-7631-987b-8c0f96bab914", "/ask" })
        {
            var response = await client.GetAsync(new Uri(page, UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("<wi-root>", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(WebAppHosting.ShellContentSecurityPolicy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
            Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
            Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        }

        var script = await client.GetAsync(new Uri("/main-ABC123.js", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Contains("immutable", script.Headers.CacheControl?.ToString(), StringComparison.Ordinal);

        var translations = await client.GetAsync(new Uri("/i18n/en.json", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, translations.StatusCode);
        Assert.Equal("no-cache", translations.Headers.CacheControl?.ToString());

        // Missing files and unknown API routes are never answered with the page (anonymous callers get 401, as before).
        foreach (var path in new[] { "/main-MISSING.js", "/api/does-not-exist", "/api" })
        {
            var response = await client.GetAsync(new Uri(path, UriKind.Relative));
            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        }

        // Health checks keep their own answers.
        var health = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("application/json", health.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/api/auth/csrf", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task WithoutTheSetting_NoPagesAreServed_AndProxiesStayLoopbackOnly()
    {
        using var factory = new ApiFactory(_db.ConnectionString);
        using var client = factory.CreateClient();
        foreach (var path in new[] { "/", "/ask" })
        {
            var response = await client.GetAsync(new Uri(path, UriKind.Relative));
            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        }

        var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        Assert.NotEmpty(options.KnownProxies);
        Assert.Equal(1, options.ForwardLimit);
    }

    [Fact]
    public void TrustAllProxies_ClearsTheKnownProxyLists_AndKeepsTheLastHopOnly()
    {
        using var factory = new ApiFactory(_db.ConnectionString, new Dictionary<string, string?> { ["ReverseProxy:TrustAllProxies"] = "true" });
        var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
        Assert.Equal(1, options.ForwardLimit);
    }
}
