using WabiInsight.Core.Entities.Ai;

namespace WabiInsight.Core.Ai;

/// <summary>
/// One request to a model: a system prompt, one user turn and (optionally) a JSON schema the answer must satisfy.
/// <see cref="SharedContext"/>, when given, is the start of the user turn that repeats from one request to the next (the planner's
/// catalogue): it is sent before <see cref="UserContent"/>, Anthropic caches it (prompt caching), and other providers receive
/// the two joined (<see cref="FullUserContent"/>).
/// </summary>
public sealed record AiRequest(string SystemPrompt, string UserContent, string? JsonSchema, string SchemaName, int MaxOutputTokens, string? Effort, string? SharedContext = null)
{
    public string FullUserContent => string.IsNullOrEmpty(SharedContext) ? UserContent : SharedContext + UserContent;
}

/// <summary>Tokens of one call. InputTokens includes the cached ones read (CachedInputTokens) and written (CacheWriteTokens).</summary>
public sealed record AiUsage(long InputTokens, long OutputTokens, long CachedInputTokens, long CacheWriteTokens = 0)
{
    public long TotalTokens => InputTokens + OutputTokens;
}

public sealed record AiCompletion(string Text, AiUsage Usage, string Model, string? StopReason);

/// <summary>Everything an adapter needs to call one endpoint. The key lives in memory only for the call.</summary>
public sealed record AiProviderDescriptor(Guid ProviderId, AiProviderType ProviderType, string? Endpoint, string Model, string ApiKey, int MaxOutputTokens, string? Effort, decimal InputPricePerMillion, decimal OutputPricePerMillion, decimal CachedInputPricePerMillion)
{
    /// <summary>Writing to the prompt cache (Anthropic, five-minute cache) costs 1.25 times the input price.</summary>
    public const decimal CacheWritePriceFactor = 1.25m;

    public decimal EstimateCost(AiUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        var uncached = Math.Max(0, usage.InputTokens - usage.CachedInputTokens - usage.CacheWriteTokens);
        return (uncached * InputPricePerMillion
            + usage.CacheWriteTokens * InputPricePerMillion * CacheWritePriceFactor
            + usage.CachedInputTokens * CachedInputPricePerMillion
            + usage.OutputTokens * OutputPricePerMillion) / 1_000_000m;
    }
}

/// <summary>Raised when the provider cannot answer: unreachable, rejected the key, refused, or returned an unusable body. Code is a stable error code.</summary>
public sealed class AiProviderException : Exception
{
    public AiProviderException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public AiProviderException()
    {
        Code = "ai_provider_error";
    }

    public AiProviderException(string message)
        : base(message)
    {
        Code = "ai_provider_error";
    }

    public AiProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "ai_provider_error";
    }

    public string Code { get; }
}

/// <summary>Adapter for one vendor protocol. Adapters never see business rows; they receive the prompt and return text.</summary>
public interface IAiProvider
{
    AiProviderType ProviderType { get; }

    Task<AiCompletion> CompleteAsync(AiProviderDescriptor descriptor, AiRequest request, CancellationToken cancellationToken = default);
}

public interface IAiProviderFactory
{
    IAiProvider Resolve(AiProviderType providerType);
}

public sealed record ResolvedAiProvider(AiProviderDescriptor Descriptor, IAiProvider Provider, AiProviderScope Scope, string Name);

/// <summary>Picks the provider an organization's requests go to: its own selection inside the allow-list, else the approved platform default. Never a fallback to an unapproved endpoint.</summary>
public interface IAiProviderResolver
{
    Task<ResolvedAiProvider> ResolveForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
}

public sealed record AiProviderDto(Guid ProviderId, AiProviderScope Scope, Guid? OrganizationId, AiProviderType ProviderType, string Name, string? Endpoint, string Model, bool ApiKeyConfigured, string? ApiKeyHint, decimal InputPricePerMillion, decimal OutputPricePerMillion, decimal CachedInputPricePerMillion, int MaxOutputTokens, string? Effort, bool IsEnabled, bool IsApproved, bool IsDefault, DateTimeOffset? LastTestAt, bool? LastTestSucceeded, string? LastTestDetail, DateTimeOffset UpdatedAt);

public sealed record SaveAiProviderCommand(AiProviderType ProviderType, string Name, string? Endpoint, string Model, string? ApiKey, decimal InputPricePerMillion, decimal OutputPricePerMillion, decimal CachedInputPricePerMillion, int MaxOutputTokens, string? Effort, bool IsEnabled, bool IsApproved);

public sealed record AiProviderTestResult(bool Succeeded, string? Model, long InputTokens, long OutputTokens, double DurationMs, string? Error);

public sealed record OrganizationAiSettingsDto(Guid? SelectedProviderId, IReadOnlyList<AiProviderDto> PlatformProviders, IReadOnlyList<AiProviderDto> OwnProviders, IReadOnlyList<AiProviderType> AllowedTypes);

/// <summary>Platform and organization management of provider configurations.</summary>
public interface IAiProviderService
{
    // Platform administrators
    Task<IReadOnlyList<AiProviderDto>> ListPlatformAsync(CancellationToken cancellationToken = default);

    Task<AiProviderDto> CreatePlatformAsync(SaveAiProviderCommand command, CancellationToken cancellationToken = default);

    Task<AiProviderDto> UpdatePlatformAsync(Guid providerId, SaveAiProviderCommand command, CancellationToken cancellationToken = default);

    Task DeletePlatformAsync(Guid providerId, CancellationToken cancellationToken = default);

    Task<AiProviderDto> SetDefaultAsync(Guid providerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiProviderType>> GetAllowedTypesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiProviderType>> SetAllowedTypesAsync(IReadOnlyCollection<AiProviderType> types, CancellationToken cancellationToken = default);

    // Organization administrators
    Task<OrganizationAiSettingsDto> GetOrganizationSettingsAsync(CancellationToken cancellationToken = default);

    Task<AiProviderDto> CreateOrganizationAsync(SaveAiProviderCommand command, CancellationToken cancellationToken = default);

    Task<AiProviderDto> UpdateOrganizationAsync(Guid providerId, SaveAiProviderCommand command, CancellationToken cancellationToken = default);

    Task DeleteOrganizationAsync(Guid providerId, CancellationToken cancellationToken = default);

    Task<OrganizationAiSettingsDto> SelectForOrganizationAsync(Guid? providerId, CancellationToken cancellationToken = default);

    /// <summary>Sends a one-line prompt and records the outcome on the row. Either side may test the rows it can see.</summary>
    Task<AiProviderTestResult> TestAsync(Guid providerId, CancellationToken cancellationToken = default);
}
