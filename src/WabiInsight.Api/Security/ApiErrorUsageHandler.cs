using Microsoft.AspNetCore.Diagnostics;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Usage;

namespace WabiInsight.Api.Security;

/// <summary>
/// Records unhandled exceptions as Api.Error usage events (for the platform error trend) and lets the
/// default problem-details handler produce the 500 response. Exception text is never sent to the client.
/// </summary>
public sealed partial class ApiErrorUsageHandler : IExceptionHandler
{
    private readonly ILogger<ApiErrorUsageHandler> _logger;

    public ApiErrorUsageHandler(ILogger<ApiErrorUsageHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>UsageEvents.EntityId is 64 characters; long paths keep their full value in the metadata only.</summary>
    private static string EntityId(PathString path)
    {
        var value = path.Value ?? "/";
        return value.Length <= 64 ? value : value[..64];
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        try
        {
            // Whatever the failed operation had staged must not be saved together with the error event.
            httpContext.RequestServices.GetService<Infrastructure.Persistence.ConfigurationDbContext>()?.ChangeTracker.Clear();
            var usage = httpContext.RequestServices.GetRequiredService<IUsageEventWriter>();
            await usage.AppendAndSaveAsync(
                UsageEventTypes.ApiError,
                "Request",
                EntityId(httpContext.Request.Path),
                succeeded: false,
                metadata: new { method = httpContext.Request.Method, path = httpContext.Request.Path.Value, exception = exception.GetType().Name },
                cancellationToken: cancellationToken);
        }
        catch (Exception recordingFailure) when (recordingFailure is not OperationCanceledException)
        {
            LogRecordingFailed(_logger, recordingFailure);
        }

        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not record an Api.Error usage event.")]
    private static partial void LogRecordingFailed(ILogger logger, Exception exception);
}
