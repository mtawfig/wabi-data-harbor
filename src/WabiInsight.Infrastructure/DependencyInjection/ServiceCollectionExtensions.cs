using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Authentication;
using WabiInsight.Core.Bootstrap;
using WabiInsight.Core.Common;
using WabiInsight.Core.Email;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Security;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Audit;
using WabiInsight.Infrastructure.Authentication;
using WabiInsight.Infrastructure.Bootstrap;
using WabiInsight.Infrastructure.Email;
using WabiInsight.Core.Platform;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Health;
using WabiInsight.Infrastructure.Platform;
using WabiInsight.Infrastructure.Usage;
using WabiInsight.Infrastructure.Organizations;
using WabiInsight.Infrastructure.Persistence;
using WabiInsight.Infrastructure.Security;
using WabiInsight.Core.Notifications;
using WabiInsight.Core.Storage;
using WabiInsight.Core.Support;
using WabiInsight.Infrastructure.Notifications;
using WabiInsight.Infrastructure.Storage;
using WabiInsight.Infrastructure.Support;
using WabiInsight.Core.SupportAccess;
using WabiInsight.Infrastructure.SupportAccess;

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
        services.AddScoped<IAdministratorAccountService, AdministratorAccountService>();
        services.AddScoped<IOrganizationUserProvisioningService, OrganizationUserProvisioningService>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IOrganizationStructureService, OrganizationStructureService>();
        services.AddScoped<IOrganizationUserService, OrganizationUserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IRoleTemplateService, RoleTemplateService>();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<PlatformUrlOptions>()
            .Bind(configuration.GetSection(PlatformUrlOptions.SectionName));

        var provider = configuration.GetSection(EmailOptions.SectionName)["Provider"] ?? "None";
        switch (provider.Trim().ToUpperInvariant())
        {
            case "SMTP":
                services.TryAddSingleton<IEmailSender, SmtpEmailSender>();
                break;
            case "FILE":
                services.TryAddSingleton<IEmailSender, FileEmailSender>();
                break;
            default:
                services.TryAddSingleton<IEmailSender, NullEmailSender>();
                break;
        }

        services.AddSingleton<EmailDeliveryChannel>();
        services.AddScoped<IEmailOutbox, EmailOutbox>();
        services.AddHostedService<EmailOutboxWorker>();
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
            .AddDbContextCheck<ConfigurationDbContext>("configuration-database", tags: ["ready"])
            .AddCheck<EmailConfigurationHealthCheck>("email-provider", tags: ["ops"])
            .AddCheck<EmailOutboxHealthCheck>("email-outbox", tags: ["ops"]);

        services.AddScoped<IUsageEventWriter, UsageEventWriter>();
        services.AddScoped<IPlatformMetricsService, PlatformMetricsService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<IPlatformHealthService, PlatformHealthService>();

        services.AddOptions<SupportDeskOptions>().Bind(configuration.GetSection(SupportDeskOptions.SectionName));
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.SectionName));
        services.TryAddSingleton<IArtifactStorage, FileSystemArtifactStorage>();
        services.TryAddSingleton<IAttachmentScanner, NoOpAttachmentScanner>();
        services.AddScoped<ISupportTicketService, SupportTicketService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

        services.AddOptions<SupportAccessOptions>().Bind(configuration.GetSection(SupportAccessOptions.SectionName));
        services.AddScoped<ISupportAccessService, SupportAccessService>();
        services.AddScoped<ISupportAccessAuthorizer, SupportAccessAuthorizer>();
        services.AddScoped<ISupportOperationExecutor, SupportOperationExecutor>();
        services.AddHostedService<SupportAccessExpiryWorker>();

        return services;
    }
}
