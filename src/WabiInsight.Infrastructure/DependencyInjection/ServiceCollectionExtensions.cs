using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WabiInsight.Infrastructure.Health;

namespace WabiInsight.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Layer 3 integrations (configuration database, providers, storage).
    /// P1.1 registers only the self health check; P1.2 adds the EF Core configuration database.
    /// </summary>
    public static IServiceCollection AddWabiInsightInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHealthChecks()
            .AddCheck<ApiSelfHealthCheck>("api", tags: ["live", "ready"]);

        return services;
    }
}
