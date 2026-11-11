using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Applications;
using WabiInsight.Core.Common;
using WabiInsight.Infrastructure.Authentication;
using WabiInsight.Infrastructure.Insights;
using WabiInsight.Infrastructure.Persistence;
using WabiInsight.Infrastructure.Storage;

namespace WabiInsight.Infrastructure.Health;

/// <summary>One setting that still has a value meant for a developer's machine. The message never contains a secret.</summary>
public sealed record DeploymentFinding(string Code, string Message);

/// <summary>
/// Settings a deployed server must replace (review S5): each has a default that works on a developer's machine and
/// fails quietly on a server (links to localhost in e-mails, files and keys inside the folder a deployment replaces,
/// schedules in UTC). Found at start-up and shown on the System Health page; never a reason to refuse requests.
/// </summary>
public static class DeploymentSettings
{
    public static IReadOnlyList<DeploymentFinding> Find(PlatformUrlOptions urls, StorageOptions storage, DataProtectionOptions dataProtection, InsightsOptions insights, bool ianaTimeZones)
    {
        ArgumentNullException.ThrowIfNull(urls);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(dataProtection);
        ArgumentNullException.ThrowIfNull(insights);
        var findings = new List<DeploymentFinding>();

        if (!Uri.TryCreate(urls.BaseUrl, UriKind.Absolute, out var baseUrl) || baseUrl.IsLoopback || baseUrl.Scheme != Uri.UriSchemeHttps)
        {
            findings.Add(new("platform_base_url", "Platform:BaseUrl is not the public https address of this site: links in e-mails will not open."));
        }

        if (!storage.UsesAzureBlob && !Path.IsPathFullyQualified(storage.RootPath))
        {
            findings.Add(new("storage_root_relative", "Storage:RootPath is a relative path: generated and uploaded files are kept inside the application folder, which a deployment replaces."));
        }

        if (!Path.IsPathFullyQualified(dataProtection.KeyRingPath))
        {
            findings.Add(new("key_ring_relative", "DataProtection:KeyRingPath is a relative path: the keys are kept inside the application folder, and stored connection strings become unreadable when a deployment replaces it."));
        }

        if (string.IsNullOrWhiteSpace(insights.ConnectionString))
        {
            findings.Add(new("insights_login_missing", "Insights:ConnectionString is empty: Platform Insights queries run with the application's own database login instead of the read-only one (scripts/sql/create-insights-login.sql)."));
        }

        if (!ianaTimeZones)
        {
            findings.Add(new("time_zone_names", "This server does not resolve IANA time zone names (the .NET runtime runs without ICU): only the time zones offered on the organization form are supported, any other runs as UTC."));
        }

        return findings;
    }
}

/// <summary>Degraded on the System Health page while a deployment setting still has its development value.</summary>
public sealed class DeploymentSettingsHealthCheck : IHealthCheck
{
    private readonly IReadOnlyList<DeploymentFinding> _findings;

    public DeploymentSettingsHealthCheck(IOptions<PlatformUrlOptions> urls, IOptions<StorageOptions> storage, IOptions<DataProtectionOptions> dataProtection, IOptions<InsightsOptions> insights)
    {
        _findings = DeploymentSettings.Find(urls.Value, storage.Value, dataProtection.Value, insights.Value, TimeZones.IanaNamesAvailable);
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(_findings.Count == 0
            ? HealthCheckResult.Healthy("Deployment settings are in place.")
            : HealthCheckResult.Degraded(string.Join(' ', _findings.Select(f => f.Message)), data: new Dictionary<string, object> { ["codes"] = _findings.Select(f => f.Code).ToArray() }));
}

/// <summary>Writes each finding to the log once at start-up, outside Development and the test host.</summary>
public sealed partial class DeploymentSettingsReporter : IHostedService
{
    private readonly IHostEnvironment _environment;
    private readonly IOptions<PlatformUrlOptions> _urls;
    private readonly IOptions<StorageOptions> _storage;
    private readonly IOptions<DataProtectionOptions> _dataProtection;
    private readonly IOptions<InsightsOptions> _insights;
    private readonly ILogger<DeploymentSettingsReporter> _logger;

    public DeploymentSettingsReporter(IHostEnvironment environment, IOptions<PlatformUrlOptions> urls, IOptions<StorageOptions> storage, IOptions<DataProtectionOptions> dataProtection, IOptions<InsightsOptions> insights, ILogger<DeploymentSettingsReporter> logger)
    {
        _environment = environment;
        _urls = urls;
        _storage = storage;
        _dataProtection = dataProtection;
        _insights = insights;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_environment.IsDevelopment() || _environment.IsEnvironment("Testing"))
        {
            return Task.CompletedTask;
        }

        foreach (var finding in DeploymentSettings.Find(_urls.Value, _storage.Value, _dataProtection.Value, _insights.Value, TimeZones.IanaNamesAvailable))
        {
            LogFinding(_logger, finding.Code, finding.Message);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deployment setting to review ({Code}): {Message}")]
    private static partial void LogFinding(ILogger logger, string code, string message);
}

/// <summary>Remembers that the database schema was seen up to date, so the readiness check asks the database once.</summary>
public sealed class MigrationState
{
    public bool UpToDate { get; set; }
}

/// <summary>
/// Not ready while the database is behind the installed release (review S3): a deployment made without "Apply database
/// migrations" otherwise reports healthy and then fails on the first query that needs a new column.
/// </summary>
public sealed class PendingMigrationsHealthCheck : IHealthCheck
{
    private readonly ConfigurationDbContext _context;
    private readonly MigrationState _state;

    public PendingMigrationsHealthCheck(ConfigurationDbContext context, MigrationState state)
    {
        _context = context;
        _state = state;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_state.UpToDate)
        {
            return HealthCheckResult.Healthy("The database schema matches this release.");
        }

        var pending = (await _context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            _state.UpToDate = true;
            return HealthCheckResult.Healthy("The database schema matches this release.");
        }

        return HealthCheckResult.Unhealthy(
            $"The database is {pending.Count} migration(s) behind this release: run the 'migrate' command (deploy again with database migrations applied).",
            data: new Dictionary<string, object> { ["pending"] = pending.Count, ["next"] = pending[0] });
    }
}
