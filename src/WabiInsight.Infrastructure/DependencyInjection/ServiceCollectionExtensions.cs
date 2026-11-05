using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Authentication;
using WabiInsight.Core.Bootstrap;
using WabiInsight.Core.Common;
using WabiInsight.Core.Security;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Audit;
using WabiInsight.Infrastructure.Authentication;
using WabiInsight.Infrastructure.Bootstrap;
using WabiInsight.Infrastructure.Health;
using WabiInsight.Infrastructure.Persistence;
using WabiInsight.Infrastructure.Security;

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
        services.AddSingleton<IPasswordHashingService, IdentityPasswordHashingService>();

        services.AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<BootstrapOptions>()
            .Bind(configuration.GetSection(BootstrapOptions.SectionName));

        services.TryAddScoped<IRequestContext, NullRequestContext>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IAccountResolver, AccountResolver>();
        services.AddScoped<IAdministratorPasswordService, AdministratorPasswordService>();
        services.AddScoped<IBootstrapService, BootstrapService>();

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
