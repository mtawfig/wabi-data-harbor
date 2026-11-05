using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WabiInsight.Core.Email;
using WabiInsight.IntegrationTests.Email;

namespace WabiInsight.IntegrationTests.Api;

/// <summary>
/// Hosts the API in-process against a SQL Server test database. The Testing environment is not
/// Development, so the sample bootstrap password is rejected exactly as in production.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string BootstrapEmail = "bootstrap@wabitex.test";
    public const string BootstrapPassword = "Bootstrap#Strong#Pass#2026";
    public const string BootstrapSecret = "test-initialization-secret-0123456789";

    private readonly string _connectionString;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    /// <summary>Every email the API sends during the test lands here instead of SMTP.</summary>
    public RecordingEmailSender Emails { get; } = new();

    public ApiFactory(string connectionString, IReadOnlyDictionary<string, string?>? settings = null)
    {
        _connectionString = connectionString;
        _settings = settings ?? new Dictionary<string, string?>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:ConfigurationDatabase", _connectionString);

        // Generous limits so unrelated tests never trip the per-client limiter; dedicated tests lower them.
        builder.UseSetting("Security:LoginRateLimitPerMinute", "1000");
        builder.UseSetting("Security:BootstrapRateLimitPerMinute", "1000");
        // Re-validate the security stamp on every request so session-invalidation tests are deterministic.
        builder.UseSetting("Security:SecurityStampValidationMinutes", "0");

        // Email: the outbox worker runs for real, delivery is captured in memory.
        builder.UseSetting("Email:Provider", "Test");
        builder.UseSetting("Email:FromEmail", "no-reply@wabitex.test");
        builder.UseSetting("Email:RetryDelaySeconds", "1");
        builder.UseSetting("Email:MaxAttempts", "2");

        builder.UseSetting("Bootstrap:Enabled", "true");
        builder.UseSetting("Bootstrap:AdministratorEmail", BootstrapEmail);
        builder.UseSetting("Bootstrap:AdministratorFullName", "Bootstrap Administrator");
        builder.UseSetting("Bootstrap:AdministratorPassword", BootstrapPassword);
        builder.UseSetting("Bootstrap:InitializationSecret", BootstrapSecret);

        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }
}
