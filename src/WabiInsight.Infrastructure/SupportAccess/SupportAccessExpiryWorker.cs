using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WabiInsight.Core.SupportAccess;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Infrastructure.SupportAccess;

/// <summary>Marks grants past their expiry as Expired every minute so lists and sessions stay truthful even without traffic.</summary>
public sealed partial class SupportAccessExpiryWorker : BackgroundService
{
    /// <summary>Well-known scheduler principal: a platform-scope actor for background expiry, recorded as such in audit.</summary>
    public static readonly TenantPrincipal SchedulerPrincipal = TenantPrincipal.ForPlatformAdministrator(Guid.Parse("00000000-0000-0000-0000-000000000002"), "scheduler");

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SupportAccessExpiryWorker> _logger;

    public SupportAccessExpiryWorker(IServiceScopeFactory scopes, ILogger<SupportAccessExpiryWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ITenantContextInitializer>().Initialize(SchedulerPrincipal);
                var expired = await scope.ServiceProvider.GetRequiredService<ISupportAccessService>().ExpireAsync(stoppingToken);
                if (expired > 0)
                {
                    LogExpired(_logger, expired);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogFailure(_logger, ex);
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Expired {Count} support access grant(s).")]
    private static partial void LogExpired(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Support access expiry sweep failed.")]
    private static partial void LogFailure(ILogger logger, Exception exception);
}
