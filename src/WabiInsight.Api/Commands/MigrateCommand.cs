using Microsoft.EntityFrameworkCore;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Api.Commands;

/// <summary>
/// `dotnet WabiInsight.Api.dll migrate` applies pending EF Core migrations and exits.
/// Migrations are applied at deployment time, never by a web request (specification §7.2).
/// </summary>
internal static partial class MigrateCommand
{
    public const string Name = "migrate";

    public static async Task<int> RunAsync(WebApplication app, CancellationToken cancellationToken)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Migrate");

        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            LogUpToDate(logger);
            return 0;
        }

        LogApplying(logger, pending.Count, pending);
        await context.Database.MigrateAsync(cancellationToken);
        LogApplied(logger);
        return 0;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Configuration database is up to date.")]
    private static partial void LogUpToDate(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {Count} migration(s): {Migrations}")]
    private static partial void LogApplying(ILogger logger, int count, IReadOnlyList<string> migrations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrations applied.")]
    private static partial void LogApplied(ILogger logger);
}
