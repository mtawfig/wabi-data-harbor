using System.Net;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Auth;

[Collection(DatabaseCollectionDefinition.Name)]
public sealed class RateLimitTests
{
    private readonly SqlServerTestDatabase _db;

    public RateLimitTests(SqlServerTestDatabase db)
    {
        _db = db;
    }

    [Fact]
    public async Task Login_IsRateLimited_PerClient()
    {
        using var factory = new ApiFactory(_db.ConnectionString, new Dictionary<string, string?> { ["Security:LoginRateLimitPerMinute"] = "2" });
        using var client = factory.CreateClient();
        await client.RefreshCsrfAsync();

        var first = await client.PostJsonAsync("/api/auth/login", new { email = TestData.BobEmail, password = "wrong-password-123456" });
        var second = await client.PostJsonAsync("/api/auth/login", new { email = TestData.BobEmail, password = "wrong-password-123456" });
        var third = await client.PostJsonAsync("/api/auth/login", new { email = TestData.BobEmail, password = "wrong-password-123456" });

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }
}
