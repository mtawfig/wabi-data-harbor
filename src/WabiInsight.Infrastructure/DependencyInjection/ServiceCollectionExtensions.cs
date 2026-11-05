using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WabiInsight.Core.Common;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Health;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public const string ConfigurationDatabaseConnectionName = "ConfigurationDatabase";

    /// <summary>
    /// Registers Layer 3 integrations: the configuration database, tenant context, clock and health checks.
    /// </summary>
    public static IServiceCollection AddWabiInsightInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IClock, SystemClock>();

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantContextInitializer>(sp => sp.GetRequiredService<TenantContext>());

        var connectionString = configuration.GetConnectionString(ConfigurationDatabaseConnectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConfigurationDatabaseConnectionName}' is not configured. " +
                "Set ConnectionStrings:ConfigurationDatabase via user secrets or environment variables.");
        }

        services.AddDbContext<ConfigurationDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(ConfigurationDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure(maxRetryCount: 3);
                sql.CommandTimeout(30);
            }));

        services.AddHealthChecks()
            .AddCheck<ApiSelfHealthCheck>("api", tags: ["live", "ready"])
            .AddDbContextCheck<ConfigurationDbContext>("configuration-database", tags: ["ready"]);

        return services;
    }
}
