using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace WabiInsight.Infrastructure.Health;

/// <summary>
/// Liveness check for the API process itself. Database, AI provider, storage and email
/// checks are registered by later phases alongside the services they verify.
/// </summary>
public sealed class ApiSelfHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HealthCheckResult.Healthy("API process is running."));
    }
}
