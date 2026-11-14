using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Ai;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Ai;

public sealed class AiCallScope : IAiCallScope
{
    public Guid? RunId { get; set; }
}

/// <summary>
/// Writes the AI usage ledger (P13.48). A row is saved at once, through its own short-lived database context: it does
/// not wait for, and is not lost with, the unit of work of the feature that made the call. A question that fails
/// after its plan was written has still paid for the plan.
/// </summary>
public sealed class AiCallRecorder : IAiCallRecorder
{
    private readonly DbContextOptions<ConfigurationDbContext> _options;
    private readonly ITenantContext _tenantContext;
    private readonly IAiCallScope _scope;
    private readonly IClock _clock;

    public AiCallRecorder(DbContextOptions<ConfigurationDbContext> options, ITenantContext tenantContext, IAiCallScope scope, IClock clock)
    {
        _options = options;
        _tenantContext = tenantContext;
        _scope = scope;
        _clock = clock;
    }

    public async Task RecordAsync(AiProviderDescriptor descriptor, AiRequest request, AiCompletion? completion, string? failureCode, double durationMs)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(request);
        var principal = _tenantContext.Principal;
        var organizationId = request.OrganizationId ?? principal.OrganizationId;
        var usage = completion?.Usage ?? new AiUsage(0, 0, 0);
        // Rounded to what the columns keep, so the stored total is exactly the stored input plus the stored output.
        var inputCost = Math.Round(descriptor.EstimateInputCost(usage), 6);
        var outputCost = Math.Round(descriptor.EstimateOutputCost(usage), 6);
        var call = new AiCall
        {
            CallId = Ids.New(),
            OrganizationId = organizationId,
            // The user is recorded only inside their own organization: a Platform Administrator's test has none.
            UserId = organizationId is not null && principal.OrganizationId == organizationId ? principal.UserId : null,
            Purpose = request.Purpose,
            RunId = _scope.RunId,
            ProviderId = descriptor.ProviderId == Guid.Empty ? null : descriptor.ProviderId,
            ProviderType = descriptor.ProviderType.ToString(),
            Model = Limit(completion?.Model ?? descriptor.Model, 200) ?? string.Empty,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            CachedInputTokens = usage.CachedInputTokens,
            CacheWriteTokens = usage.CacheWriteTokens,
            InputCost = inputCost,
            OutputCost = outputCost,
            TotalCost = inputCost + outputCost,
            InputPricePerMillion = descriptor.InputPricePerMillion,
            OutputPricePerMillion = descriptor.OutputPricePerMillion,
            CachedInputPricePerMillion = descriptor.CachedInputPricePerMillion,
            Succeeded = completion is not null,
            FailureCode = Limit(failureCode, 100),
            StopReason = Limit(completion?.StopReason, 50),
            DurationMs = durationMs,
            CreatedAt = _clock.UtcNow,
        };

        // Its own context, and no cancellation: once the provider has answered, the tokens are spent whether or not
        // the person is still waiting for the answer.
        await using var context = new ConfigurationDbContext(_options, _tenantContext, _clock);
        context.AiCalls.Add(call);
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private static string? Limit(string? value, int length) => value is null || value.Length <= length ? value : value[..length];
}

/// <summary>An adapter that records every call it makes, answered or not, in the AI usage ledger.</summary>
public sealed class RecordingAiProvider : IAiProvider
{
    private readonly IAiProvider _inner;
    private readonly IAiCallRecorder _recorder;

    public RecordingAiProvider(IAiProvider inner, IAiCallRecorder recorder)
    {
        _inner = inner;
        _recorder = recorder;
    }

    public AiProviderType ProviderType => _inner.ProviderType;

    public async Task<AiCompletion> CompleteAsync(AiProviderDescriptor descriptor, AiRequest request, CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        AiCompletion completion;
        try
        {
            completion = await _inner.CompleteAsync(descriptor, request, cancellationToken);
        }
        catch (Exception ex)
        {
            // No answer means no usage to charge; the attempt is still on the ledger.
            var code = ex switch { AiProviderException provider => provider.Code, OperationCanceledException => "cancelled", _ => "ai_provider_error" };
            await _recorder.RecordAsync(descriptor, request, null, code, watch.Elapsed.TotalMilliseconds);
            throw;
        }

        await _recorder.RecordAsync(descriptor, request, completion, null, watch.Elapsed.TotalMilliseconds);
        return completion;
    }
}

/// <summary>The factory the application uses: every adapter it hands out is wrapped in <see cref="RecordingAiProvider"/>.</summary>
public sealed class RecordingAiProviderFactory : IAiProviderFactory
{
    private readonly AiProviderFactory _inner;
    private readonly IAiCallRecorder _recorder;

    public RecordingAiProviderFactory(AiProviderFactory inner, IAiCallRecorder recorder)
    {
        _inner = inner;
        _recorder = recorder;
    }

    public IAiProvider Resolve(AiProviderType providerType) => new RecordingAiProvider(_inner.Resolve(providerType), _recorder);
}
