using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Api;

[Collection(DatabaseCollectionDefinition.Name)]
public sealed class HealthAndInfoTests : IDisposable
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public HealthAndInfoTests(SqlServerTestDatabase db)
    {
        _factory = new ApiFactory(db.ConnectionString);
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpoints_ReportHealthy(string path)
    {
        var response = await _client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", document.RootElement.GetProperty("status").GetString());
        var checks = document.RootElement.GetProperty("checks").EnumerateArray()
            .Select(check => check.GetProperty("name").GetString())
            .ToList();
        Assert.Contains("api", checks);
        if (path == "/health/ready")
        {
            Assert.Contains("configuration-database", checks);
            Assert.Contains("database-schema", checks);
            // P13.33: the folders the application writes to are part of being ready.
            Assert.Contains("file-storage", checks);
            Assert.Contains("key-ring", checks);
            // Settings to review never make the site "not ready".
            Assert.DoesNotContain("deployment-settings", checks);
        }
    }

    [Fact]
    public async Task PlatformInfo_ReturnsProductIdentity()
    {
        var info = await _client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/platform/info", UriKind.Relative));

        Assert.Equal("Wabi Insight", info.GetProperty("productName").GetString());
        Assert.Equal("AP-AIR", info.GetProperty("platformCode").GetString());
        Assert.Equal(2, info.GetProperty("supportedLanguages").GetArrayLength());
    }

    [Fact]
    public async Task UnknownRoute_IsUnauthorized_ForAnonymous_AndNotFound_WhenAuthenticated()
    {
        // Default-deny: without a session even unknown routes are not revealed.
        var anonymous = await _client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("application/problem+json", anonymous.Content.Headers.ContentType?.MediaType);

        await _client.LoginAsync(TestData.AliceEmail, TestData.UserPassword);
        var authenticated = await _client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, authenticated.StatusCode);
        Assert.Equal("application/problem+json", authenticated.Content.Headers.ContentType?.MediaType);
    }
}
