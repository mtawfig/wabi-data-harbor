using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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
using Microsoft.AspNetCore.DataProtection;
using WabiInsight.Core.Applications;
using WabiInsight.Core.Metadata;
using WabiInsight.Infrastructure.Applications;
using WabiInsight.Infrastructure.Databases;
using WabiInsight.Infrastructure.Metadata;
using WabiInsight.Core.Permissions;
using WabiInsight.Infrastructure.Permissions;
using WabiInsight.Core.Insights;
using WabiInsight.Infrastructure.Insights;
using WabiInsight.Core.Ai;
using WabiInsight.Infrastructure.Ai;
using WabiInsight.Core.Monitoring;
using WabiInsight.Infrastructure.Monitoring;

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
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddScoped<IAdministratorAccountService, AdministratorAccountService>();
        services.AddScoped<IOrganizationUserProvisioningService, OrganizationUserProvisioningService>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IOrganizationStructureService, OrganizationStructureService>();
        services.AddScoped<IOrganizationUserService, OrganizationUserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IRoleTemplateService, RoleTemplateService>();
        services.AddScoped<Plans.ServicePlanService>();
        services.AddScoped<Core.Plans.IServicePlanService>(p => p.GetRequiredService<Plans.ServicePlanService>());
        services.AddScoped<Core.Plans.IAiSpendingGuard>(p => p.GetRequiredService<Plans.ServicePlanService>());

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

        services.TryAddSingleton<IEmailRoute, PlatformEmailRoute>();
        services.AddSingleton<EmailDeliveryChannel>();
        services.AddScoped<IEmailOutbox, EmailOutbox>();
        services.AddScoped<IEmailDeliveryLog, EmailDeliveryLog>();
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
            .AddCheck<PendingMigrationsHealthCheck>("database-schema", tags: ["ready"])
            .AddCheck<FileStorageHealthCheck>("file-storage", tags: ["ready"])
            .AddCheck<KeyRingHealthCheck>("key-ring", tags: ["ready"])
            .AddCheck<DeploymentSettingsHealthCheck>("deployment-settings", tags: ["ops"])
            .AddCheck<EmailConfigurationHealthCheck>("email-provider", tags: ["ops"])
            .AddCheck<EmailOutboxHealthCheck>("email-outbox", tags: ["ops"])
            .AddCheck<SchedulerHealthCheck>("scheduler", tags: ["ops"]);

        services.TryAddSingleton<MigrationState>();
        services.TryAddSingleton<FolderProbeCache>();
        services.AddOptions<Core.Applications.DataProtectionOptions>().Bind(configuration.GetSection(Core.Applications.DataProtectionOptions.SectionName));
        services.AddHostedService<DeploymentSettingsReporter>();

        services.AddScoped<IUsageEventWriter, UsageEventWriter>();
        services.AddScoped<IPlatformMetricsService, PlatformMetricsService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<IPlatformHealthService, PlatformHealthService>();

        services.AddOptions<SupportDeskOptions>().Bind(configuration.GetSection(SupportDeskOptions.SectionName));
        // Files go to the server disk (default) or a private Azure Blob Storage container, chosen by Storage:Provider.
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.SectionName))
            .Validate(o => o.Problem() is null, "Invalid Storage settings: see Storage:Provider and Storage:AzureBlob.");
        services.TryAddSingleton<FileSystemArtifactStorage>();
        services.TryAddSingleton<AzureBlobArtifactStorage>();
        services.TryAddSingleton<IArtifactStorage>(sp => sp.GetRequiredService<IOptions<StorageOptions>>().Value.UsesAzureBlob
            ? sp.GetRequiredService<AzureBlobArtifactStorage>()
            : sp.GetRequiredService<FileSystemArtifactStorage>());
        services.AddOptions<AttachmentScanningOptions>().Bind(configuration.GetSection(AttachmentScanningOptions.SectionName));
        services.TryAddSingleton<IAttachmentScanner>(sp =>
        {
            var scanning = sp.GetRequiredService<IOptions<AttachmentScanningOptions>>();
            return string.Equals(scanning.Value.Provider, "ClamAv", StringComparison.OrdinalIgnoreCase)
                ? ActivatorUtilities.CreateInstance<ClamAvAttachmentScanner>(sp)
                : new NoOpAttachmentScanner();
        });
        services.AddScoped<ISupportTicketService, SupportTicketService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

        services.AddOptions<SupportAccessOptions>().Bind(configuration.GetSection(SupportAccessOptions.SectionName));
        services.AddScoped<ISupportAccessService, SupportAccessService>();
        services.AddScoped<ISupportAccessAuthorizer, SupportAccessAuthorizer>();
        services.AddScoped<ISupportOperationExecutor, SupportOperationExecutor>();
        services.AddHostedService<SupportAccessExpiryWorker>();

        // Connection-string protection (specification §11): purpose-specific protector, key ring on the file system
        // outside the database, automatic rotation; old keys stay available for decryption.
        var dataProtection = configuration.GetSection(Core.Applications.DataProtectionOptions.SectionName).Get<Core.Applications.DataProtectionOptions>() ?? new Core.Applications.DataProtectionOptions();
        var keyRing = new DirectoryInfo(Path.GetFullPath(dataProtection.KeyRingPath));
        keyRing.Create();
        services.AddDataProtection()
            .SetApplicationName("WabiInsight")
            .PersistKeysToFileSystem(keyRing)
            .SetDefaultKeyLifetime(TimeSpan.FromDays(Math.Clamp(dataProtection.KeyLifetimeDays, 7, 365)));
        services.AddScoped<IConnectionStringProtector, ConnectionStringProtector>();
        services.AddSingleton<IDatabaseAdapter, SqlServerAdapter>();
        services.AddSingleton<IDatabaseAdapter, OracleAdapter>();
        services.AddOptions<InsightsOptions>().Bind(configuration.GetSection(InsightsOptions.SectionName));
        services.AddSingleton<IInsightsConnectionFactory, InsightsConnectionFactory>();
        services.AddSingleton<IDatabaseAdapter, PlatformInsightsAdapter>();
        services.AddScoped<IInsightsCatalogService, InsightsCatalogService>();
        services.AddScoped<IInsightsQueryService, InsightsQueryService>();
        services.AddScoped<IMetricGrantService, MetricGrantService>();
        services.AddOptions<AiOptions>().Bind(configuration.GetSection(AiOptions.SectionName));
        services.AddHttpClient(HttpAiProvider.HttpClientName, (sp, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiOptions>>().Value.RequestTimeoutSeconds, 10, 600));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WabiInsight/1.0");
        });
        // Organization-managed providers: every connection is checked against PublicAddressGuard, and a redirect is
        // returned as an error instead of being followed (it could point inside the network and would carry the key).
        services.AddHttpClient(HttpAiProvider.PublicOnlyHttpClientName, (sp, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiOptions>>().Value.RequestTimeoutSeconds, 10, 600));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WabiInsight/1.0");
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            ConnectCallback = PublicAddressGuard.ConnectAsync,
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        });
        services.AddSingleton<AiCredentialProtector>();
        services.AddSingleton<IAiProvider, AnthropicProvider>();
        services.AddSingleton<IAiProvider, OpenAiCompatibleProvider>();
        services.AddSingleton<IAiProvider, GeminiProvider>();
        services.AddSingleton<IAiProviderFactory, AiProviderFactory>();
        services.AddScoped<AiProviderService>();
        services.AddScoped<IAiProviderService>(sp => sp.GetRequiredService<AiProviderService>());
        services.AddScoped<IAiProviderResolver>(sp => sp.GetRequiredService<AiProviderService>());
        services.AddScoped<IQueryPlanner, QueryPlanner>();
        services.AddSingleton<Core.Ai.Compilers.ISqlDialectCompilerFactory, Core.Ai.Compilers.SqlDialectCompilerFactory>();
        services.AddScoped<IQueryExecutor, QueryExecutor>();
        services.AddScoped<Reports.ReportComposer>();
        services.AddSingleton<IReportRenderer, Reports.QuestPdfReportRenderer>();
        services.AddSingleton<IAnswerExporter, Reports.DocxExporter>();
        services.AddSingleton<IAnswerExporter, Reports.XlsxExporter>();
        services.AddScoped<IAnalysisService, AnalysisService>();
        services.AddScoped<Core.Analysis.IAnalysisHistoryService, Analysis.AnalysisHistoryService>();
        services.AddScoped<Core.Analysis.ISharingService, Analysis.SharingService>();
        services.AddHostedService<AiProviderSeeder>();
        services.AddSingleton<IDatabaseAdapterFactory, DatabaseAdapterFactory>();
        services.AddScoped<IApplicationService, ApplicationService>();
        services.AddScoped<IMetadataService, MetadataService>();
        services.AddScoped<MetadataService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<PermissionService>();
        services.AddScoped<IPermissionResolver, PermissionResolver>();

        // Phase 14 proactive agent: access ledger, nightly baselines and retention, DB-backed scheduler with leases.
        services.AddOptions<MonitoringOptions>().Bind(configuration.GetSection(MonitoringOptions.SectionName));
        services.AddScoped<IAccessFactRecorder, AccessFactRecorder>();
        services.AddScoped<ScheduledJobRunner>();
        services.AddScoped<IScheduledJobHandler, AccessBaselineJob>();
        services.AddScoped<IScheduledJobHandler, RetentionPurgeJob>();
        services.AddScoped<IScheduledJobHandler, MonitoringRuleJob>();
        services.AddScoped<IScheduledJobHandler, AlertDigestJob>();
        services.AddScoped<RuleEvaluator>();
        services.AddScoped<RuleRunner>();
        services.AddScoped<RuleHealthChecker>();
        services.AddScoped<AlertDigestBuilder>();
        services.AddScoped<IMonitoringRuleService, MonitoringRuleService>();
        services.AddScoped<IAlertService, AlertService>();
        services.AddScoped<IPlatformMonitoringService, PlatformMonitoringService>();
        services.AddScoped<IScheduledJobHandler, ReportSubscriptionJob>();
        services.AddScoped<PeriodicReportRenderer>();
        services.AddScoped<PeriodicReportRunner>();
        services.AddScoped<IPeriodicReportService, PeriodicReportService>();
        services.AddHostedService<JobSchedulerWorker>();

        return services;
    }
}
