using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Ai;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Ai;

/// <summary>
/// Turns the Ai:Anthropic settings into the platform default provider row on startup (owner decision: the real
/// key is kept in appsettings.json). Creates the row once and refreshes its key, model and prices whenever
/// the settings change; administrators' other edits (name, approval, default) are left alone.
/// </summary>
public sealed partial class AiProviderSeeder : IHostedService
{
    public const string SeededName = "Anthropic (platform default)";

    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<AiOptions> _options;
    private readonly ILogger<AiProviderSeeder> _logger;

    public AiProviderSeeder(IServiceScopeFactory scopes, IOptions<AiOptions> options, ILogger<AiProviderSeeder> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var anthropic = _options.Value.Anthropic;
        if (string.IsNullOrWhiteSpace(anthropic.ApiKey))
        {
            LogNoKey(_logger);
            return;
        }

        try
        {
            using var scope = _scopes.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<AiCredentialProtector>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            if (!await context.Database.CanConnectAsync(cancellationToken))
            {
                return;
            }

            var key = anthropic.ApiKey.Trim();
            var hint = AiCredentialProtector.Hint(key);
            var row = await context.AiProviderConfigurations.SingleOrDefaultAsync(p => p.Scope == AiProviderScope.Platform && p.Name == SeededName, cancellationToken);
            if (row is null)
            {
                var hasDefault = await context.AiProviderConfigurations.AnyAsync(p => p.Scope == AiProviderScope.Platform && p.IsDefault, cancellationToken);
                row = new AiProviderConfiguration { ProviderId = Ids.New(), Scope = AiProviderScope.Platform, ProviderType = AiProviderType.Anthropic, Name = SeededName, IsApproved = true, IsEnabled = true, IsDefault = !hasDefault, Effort = _options.Value.DefaultEffort, MaxOutputTokens = _options.Value.MaxOutputTokens, CreatedAt = clock.UtcNow };
                context.AiProviderConfigurations.Add(row);
            }

            var changed = row.ApiKeyHint != hint || row.Model != anthropic.Model || row.InputPricePerMillion != anthropic.InputPricePerMillion || row.OutputPricePerMillion != anthropic.OutputPricePerMillion || row.CachedInputPricePerMillion != anthropic.CachedInputPricePerMillion || !CanUnprotect(protector, row.EncryptedApiKey);
            if (changed)
            {
                row.EncryptedApiKey = protector.Protect(key);
                row.ApiKeyHint = hint;
                row.Model = anthropic.Model;
                row.InputPricePerMillion = anthropic.InputPricePerMillion;
                row.OutputPricePerMillion = anthropic.OutputPricePerMillion;
                row.CachedInputPricePerMillion = anthropic.CachedInputPricePerMillion;
                row.UpdatedAt = clock.UtcNow;
            }

            await context.SaveChangesAsync(cancellationToken);
            LogSeeded(_logger, anthropic.Model, hint);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(_logger, ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>A key ring moved or lost makes the stored key unreadable; the settings still hold the real key, so re-protect it.</summary>
    private static bool CanUnprotect(AiCredentialProtector protector, string? payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return false;
        }

        try
        {
            protector.Unprotect(payload);
            return true;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ai:Anthropic:ApiKey is empty; no platform default AI provider seeded. Configure providers on the AI Providers page.")]
    private static partial void LogNoKey(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Platform default AI provider ready: Anthropic {Model} (key {Hint}).")]
    private static partial void LogSeeded(ILogger logger, string model, string hint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seeding the platform default AI provider failed; it will be retried at the next start.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
