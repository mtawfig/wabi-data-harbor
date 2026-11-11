using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WabiInsight.Core.Common;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Health;
using WabiInsight.Infrastructure.Persistence;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Security;

/// <summary>
/// P13.31 (deployment review): changing the interface language never extends a session, and the site is not "ready"
/// while its database is behind the installed release.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class DeploymentReadinessTests
{
    private readonly SqlServerTestDatabase _db;

    public DeploymentReadinessTests(SqlServerTestDatabase db)
    {
        _db = db;
    }

    /// <summary>The real clock moved forward by the test, so hours of session age pass in a moment.</summary>
    private sealed class MovableClock : IClock
    {
        public TimeSpan Ahead { get; set; }

        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow + Ahead;
    }

    /// <summary>The API with the production session timings (stamp checked every 30 minutes, 12 hours at most) and a clock the test moves.</summary>
    private WebApplicationFactory<Program> NewApi(MovableClock clock)
        => new ApiFactory(_db.ConnectionString, new Dictionary<string, string?> { ["Security:SecurityStampValidationMinutes"] = "30", ["Security:SessionAbsoluteHours"] = "12" })
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(clock);
            }));

    private async Task<string> InsertUserAsync()
    {
        var email = $"p1331.{Guid.NewGuid():N}@org.test";
        var userId = Ids.New();
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        context.OrganizationUsers.Add(TestData.NewUser(userId, _db.Data.OrganizationAId, email, "Session User"));
        context.LoginIdentities.Add(TestData.Identity(email, PrincipalType.OrganizationUser, userId, _db.Data.OrganizationAId, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
        return email;
    }

    private static Task<HttpResponseMessage> SetLanguageAsync(HttpClient client, string language)
        => client.PutAsJsonAsync(new Uri("/api/auth/language", UriKind.Relative), new { language });

    [Fact]
    public async Task ChangingTheLanguage_DoesNotKeepASessionAlive_AfterThePasswordChanged()
    {
        var clock = new MovableClock();
        using var api = NewApi(clock);
        var email = await InsertUserAsync();

        // The same person signed in on two devices; the password is changed on the second.
        using var oldDevice = api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await oldDevice.LoginAsync(email, TestData.UserPassword)).StatusCode);
        using var newDevice = api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await newDevice.LoginAsync(email, TestData.UserPassword)).StatusCode);
        var changed = await newDevice.PostJsonAsync("/api/auth/change-password", new { currentPassword = TestData.UserPassword, newPassword = "Changed#Strong#Pass#2026" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        await newDevice.RefreshCsrfAsync();

        // The old session has not been checked again yet (that happens every 30 minutes). Changing the language there
        // must not hand it the new security stamp or a fresh check time.
        Assert.Equal(HttpStatusCode.OK, (await SetLanguageAsync(oldDevice, "ar")).StatusCode);
        Assert.Equal("ar", (await (await oldDevice.GetAsync("/api/auth/me")).ReadJsonAsync()).GetProperty("preferredLanguage").GetString());

        clock.Ahead = TimeSpan.FromMinutes(31);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldDevice.GetAsync("/api/auth/me")).StatusCode);
        // The device where the password was changed stays signed in, and may change its language freely.
        Assert.Equal(HttpStatusCode.OK, (await newDevice.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetLanguageAsync(newDevice, "en")).StatusCode);
        clock.Ahead = TimeSpan.FromMinutes(62);
        Assert.Equal("en", (await (await newDevice.GetAsync("/api/auth/me")).ReadJsonAsync()).GetProperty("preferredLanguage").GetString());
    }

    [Fact]
    public async Task ChangingTheLanguage_DoesNotRestartTheLongestSessionLifetime()
    {
        var clock = new MovableClock();
        using var api = NewApi(clock);
        var email = await InsertUserAsync();
        using var client = api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, TestData.UserPassword)).StatusCode);

        // Eleven hours into a twelve-hour session the person switches language.
        clock.Ahead = TimeSpan.FromHours(11);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetLanguageAsync(client, "ar")).StatusCode);
        Assert.Equal("ar", (await (await client.GetAsync("/api/auth/me")).ReadJsonAsync()).GetProperty("preferredLanguage").GetString());

        // The session still ends twelve hours after the sign-in, not twelve hours after the switch.
        clock.Ahead = TimeSpan.FromHours(12) + TimeSpan.FromMinutes(5);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        // A new sign-in starts a new lifetime.
        using var again = api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await again.LoginAsync(email, TestData.UserPassword)).StatusCode);
        Assert.Equal("ar", (await (await again.GetAsync("/api/auth/me")).ReadJsonAsync()).GetProperty("preferredLanguage").GetString());
    }

    [Fact]
    public async Task ADatabaseBehindTheRelease_IsNotReady_UntilItIsMigrated()
    {
        var connectionString = new SqlConnectionStringBuilder(_db.ConnectionString) { InitialCatalog = $"WabiInsightBehind_{Guid.NewGuid():N}" }.ConnectionString;
        var options = new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(ConfigurationDbContext).Assembly.FullName))
            .Options;
        await using var context = new ConfigurationDbContext(options, new TenantContext(), new SystemClock());
        try
        {
            // A database one release behind: every migration but the last.
            var migrations = context.Database.GetMigrations().ToList();
            await context.GetService<IMigrator>().MigrateAsync(migrations[^2]);

            var state = new MigrationState();
            var behind = await new PendingMigrationsHealthCheck(context, state).CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Unhealthy, behind.Status);
            Assert.Contains("1 migration(s) behind", behind.Description, StringComparison.Ordinal);
            Assert.Contains("migrate", behind.Description, StringComparison.Ordinal);
            Assert.Equal(migrations[^1], behind.Data["next"]);
            Assert.False(state.UpToDate);

            await context.Database.MigrateAsync();
            var current = await new PendingMigrationsHealthCheck(context, state).CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Healthy, current.Status);
            // Seen up to date once, the database is not asked again on every probe.
            Assert.True(state.UpToDate);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }
}
