using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Common;
using WabiInsight.Core.Email;
using WabiInsight.Core.Entities.Email;
using WabiInsight.Core.Platform;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Health;

/// <summary>Runs the registered health checks on demand for the System Health page (specification §13.2).</summary>
public sealed class PlatformHealthService : IPlatformHealthService
{
    private readonly HealthCheckService _healthChecks;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public PlatformHealthService(HealthCheckService healthChecks, ITenantContext tenantContext, IClock clock)
    {
        _healthChecks = healthChecks;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<PlatformHealthReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsPlatformAdministrator)
        {
            throw new TenantContextException("Platform health requires a platform administrator principal.");
        }

        var report = await _healthChecks.CheckHealthAsync(cancellationToken);
        return new PlatformHealthReport(
            report.Status.ToString(),
            _clock.UtcNow,
            report.TotalDuration.TotalMilliseconds,
            report.Entries.Select(e => new HealthCheckEntry(e.Key, e.Value.Status.ToString(), e.Value.Description, e.Value.Duration.TotalMilliseconds, e.Value.Data)).ToList());
    }
}

/// <summary>Reports whether an email provider is configured (never connects to the SMTP server).</summary>
public sealed class EmailConfigurationHealthCheck : IHealthCheck
{
    private readonly EmailOptions _options;

    public EmailConfigurationHealthCheck(IOptions<EmailOptions> options)
    {
        _options = options.Value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var provider = _options.Provider.Trim().ToUpperInvariant();
        var data = new Dictionary<string, object> { ["provider"] = _options.Provider, ["from"] = _options.FromEmail };
        return Task.FromResult(provider switch
        {
            "SMTP" when string.IsNullOrWhiteSpace(_options.Host) => HealthCheckResult.Unhealthy("SMTP provider selected but Email:Host is empty.", data: data),
            "SMTP" when _options.Password is "" or "REPLACE_WITH_REAL_PASSWORD" => HealthCheckResult.Degraded("SMTP password is not set; deliveries will fail.", data: data),
            "SMTP" or "FILE" or "TEST" => HealthCheckResult.Healthy($"Email provider {_options.Provider} configured.", data),
            _ => HealthCheckResult.Degraded("Email delivery is disabled (Provider = None).", data: data),
        });
    }
}

/// <summary>Degraded when queued emails are older than the retry window or recent deliveries failed.</summary>
public sealed class EmailOutboxHealthCheck : IHealthCheck
{
    private readonly ConfigurationDbContext _context;
    private readonly IClock _clock;

    public EmailOutboxHealthCheck(ConfigurationDbContext context, IClock clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var stale = await _context.EmailMessages.AsNoTracking().LongCountAsync(e => e.Status == EmailDeliveryStatus.Queued && e.CreatedAt < now.AddMinutes(-10), cancellationToken);
        var failedLastDay = await _context.EmailMessages.AsNoTracking().LongCountAsync(e => e.Status == EmailDeliveryStatus.Failed && e.CreatedAt >= now.AddDays(-1), cancellationToken);
        // Part of failedLast24h: messages the API was still holding when it stopped or restarted (marked by the outbox worker at start-up).
        var notSentBeforeRestart = failedLastDay == 0
            ? 0
            : await _context.EmailMessages.AsNoTracking().LongCountAsync(e => e.Status == EmailDeliveryStatus.Failed && e.CreatedAt >= now.AddDays(-1) && e.LastError == EmailFailureReasons.ProcessRestartedBeforeDelivery, cancellationToken);
        var data = new Dictionary<string, object> { ["staleQueued"] = stale, ["failedLast24h"] = failedLastDay, ["notSentBeforeRestartLast24h"] = notSentBeforeRestart };
        if (stale > 0)
        {
            return HealthCheckResult.Degraded($"{stale} email(s) queued for more than 10 minutes.", data: data);
        }

        if (failedLastDay == 0)
        {
            return HealthCheckResult.Healthy("Outbox is draining normally.", data);
        }

        return HealthCheckResult.Degraded(
            notSentBeforeRestart > 0
                ? $"{failedLastDay} email delivery failure(s) in the last 24 hours; {notSentBeforeRestart} of them were not sent because the service restarted and have to be sent again."
                : $"{failedLastDay} email delivery failure(s) in the last 24 hours.",
            data: data);
    }
}
