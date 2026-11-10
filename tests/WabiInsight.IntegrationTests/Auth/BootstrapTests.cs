using System.Net;
using WabiInsight.Core.Security;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Auth;

[Collection(EmptyDatabaseCollectionDefinition.Name)]
public sealed class BootstrapTests
{
    private readonly EmptySqlServerTestDatabase _db;

    public BootstrapTests(EmptySqlServerTestDatabase db)
    {
        _db = db;
    }

    [Fact]
    public async Task SamplePassword_IsRejected_OutsideDevelopment()
    {
        using var factory = new ApiFactory(_db.ConnectionString, new Dictionary<string, string?>
        {
            ["Bootstrap:AdministratorPassword"] = PasswordPolicy.SamplePassword,
        });
        using var client = factory.CreateClient();
        await client.RefreshCsrfAsync();

        var response = await client.PostJsonAsync("/api/bootstrap/initialize", new { initializationSecret = ApiFactory.BootstrapSecret });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("password_is_sample", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bootstrap_IsRefused_WhenDisabled()
    {
        using var factory = new ApiFactory(_db.ConnectionString, new Dictionary<string, string?> { ["Bootstrap:Enabled"] = "false" });
        using var client = factory.CreateClient();
        await client.RefreshCsrfAsync();

        var response = await client.PostJsonAsync("/api/bootstrap/initialize", new { initializationSecret = ApiFactory.BootstrapSecret });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_RunsOnce_ThenForcesPasswordChange()
    {
        using var factory = new ApiFactory(_db.ConnectionString);
        using var client = factory.CreateClient();
        await client.RefreshCsrfAsync();

        var before = await (await client.GetAsync("/api/bootstrap/status")).ReadJsonAsync();
        Assert.False(before.GetProperty("bootstrapCompleted").GetBoolean());
        Assert.False(before.GetProperty("pendingMigrations").GetBoolean());

        var wrongSecret = await client.PostJsonAsync("/api/bootstrap/initialize", new { initializationSecret = "not-the-secret-0123456789" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongSecret.StatusCode);

        var concurrent = await Task.WhenAll(
            client.PostJsonAsync("/api/bootstrap/initialize", new { initializationSecret = ApiFactory.BootstrapSecret }),
            factory.CreateClient().WithCsrfAsync().ContinueWith(t => t.Result.PostJsonAsync("/api/bootstrap/initialize", new { initializationSecret = ApiFactory.BootstrapSecret }), TaskScheduler.Default).Unwrap());
        Assert.Single(concurrent, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(concurrent, r => r.StatusCode == HttpStatusCode.Conflict);

        var again = await client.PostJsonAsync("/api/bootstrap/initialize", new { initializationSecret = ApiFactory.BootstrapSecret });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        // Anonymous callers learn that set-up is done, and nothing about who the administrator is (P13.25).
        var after = await client.GetAsync("/api/bootstrap/status");
        var afterBody = await after.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ApiFactory.BootstrapEmail, afterBody, StringComparison.OrdinalIgnoreCase);
        var afterJson = await after.ReadJsonAsync();
        Assert.True(afterJson.GetProperty("bootstrapCompleted").GetBoolean());
        foreach (var withheld in new[] { "bootstrapAdministratorEmail", "schemaVersion", "bootstrapCompletedAt" })
        {
            Assert.False(afterJson.TryGetProperty(withheld, out var value) && value.ValueKind != System.Text.Json.JsonValueKind.Null, withheld);
        }

        var login = await client.LoginAsync(ApiFactory.BootstrapEmail, ApiFactory.BootstrapPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await login.ReadJsonAsync()).GetProperty("mustChangePassword").GetBoolean());

        var blocked = await client.PostJsonAsync($"/api/platform/administrators/{Guid.NewGuid()}/reset-password", new { });
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

        var change = await client.PostJsonAsync("/api/auth/change-password", new { currentPassword = ApiFactory.BootstrapPassword, newPassword = "Bootstrap#Changed#Pass#2026" });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostJsonAsync($"/api/platform/administrators/{Guid.NewGuid()}/reset-password", new { })).StatusCode);

        // A signed-in Platform Administrator sees the full status (the platform settings page shows it).
        var detailed = await (await client.GetAsync("/api/bootstrap/status")).ReadJsonAsync();
        Assert.Equal(ApiFactory.BootstrapEmail, detailed.GetProperty("bootstrapAdministratorEmail").GetString());
        Assert.Matches(@"^\d{14}_\w+$", detailed.GetProperty("schemaVersion").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.String, detailed.GetProperty("bootstrapCompletedAt").ValueKind);
    }
}

internal static class BootstrapClientExtensions
{
    public static async Task<HttpClient> WithCsrfAsync(this HttpClient client)
    {
        await client.RefreshCsrfAsync();
        return client;
    }
}
