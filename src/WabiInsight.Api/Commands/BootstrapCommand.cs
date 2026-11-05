using Microsoft.Extensions.Options;
using WabiInsight.Core.Bootstrap;

namespace WabiInsight.Api.Commands;

/// <summary>
/// `dotnet WabiInsight.Api.dll bootstrap` creates the first Platform Administrator from the Bootstrap
/// configuration (headless deployments). Uses the configured initialization secret as its authorization.
/// </summary>
internal static partial class BootstrapCommand
{
    public const string Name = "bootstrap";

    public static async Task<int> RunAsync(WebApplication app, CancellationToken cancellationToken)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IBootstrapService>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<BootstrapOptions>>().Value;
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap");

        var result = await service.InitializeAsync(options.InitializationSecret, cancellationToken);
        if (result.Outcome == BootstrapOutcome.Completed)
        {
            LogCompleted(logger, result.AdministratorId!.Value);
            return 0;
        }

        LogFailed(logger, result.Outcome, string.Join(", ", result.Errors));
        return 1;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Bootstrap completed. AdministratorId={AdministratorId}. The administrator must change the password at first sign-in.")]
    private static partial void LogCompleted(ILogger logger, Guid administratorId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Bootstrap failed: {Outcome} ({Errors})")]
    private static partial void LogFailed(ILogger logger, BootstrapOutcome outcome, string errors);
}
