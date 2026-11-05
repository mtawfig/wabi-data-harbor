namespace WabiInsight.Core.Entities.Ai;

public enum AiProviderType
{
    Anthropic = 1,
    OpenAiCompatible = 2,
    Gemini = 3,
}

public enum AiProviderScope
{
    Platform = 1,
    Organization = 2,
}

/// <summary>
/// An approved model endpoint (specification §18). Platform-scoped rows are maintained by Platform
/// Administrators and shared by every organization; organization-scoped rows belong to one organization
/// (its own key, a provider type inside the platform allow-list). Credentials are Data Protection encrypted
/// and never returned by the API; <see cref="ApiKeyHint"/> keeps the last characters for recognition only.
/// </summary>
public class AiProviderConfiguration
{
    public Guid ProviderId { get; set; }

    public AiProviderScope Scope { get; set; }

    /// <summary>Null for platform-scoped rows; required for organization-scoped rows.</summary>
    public Guid? OrganizationId { get; set; }

    public AiProviderType ProviderType { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Base URL for OpenAI-compatible and self-hosted endpoints; null means the vendor default.</summary>
    public string? Endpoint { get; set; }

    public string Model { get; set; } = string.Empty;

    public string? EncryptedApiKey { get; set; }

    public string? ApiKeyHint { get; set; }

    public decimal InputPricePerMillion { get; set; }

    public decimal OutputPricePerMillion { get; set; }

    public decimal CachedInputPricePerMillion { get; set; }

    public int MaxOutputTokens { get; set; } = 4096;

    /// <summary>Reasoning effort hint (low, medium, high) for providers that support it.</summary>
    public string? Effort { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Platform approval: only approved providers may ever receive organization metadata (no fallback).</summary>
    public bool IsApproved { get; set; }

    /// <summary>The platform default used by organizations without their own selection. At most one row.</summary>
    public bool IsDefault { get; set; }

    public DateTimeOffset? LastTestAt { get; set; }

    public bool? LastTestSucceeded { get; set; }

    public string? LastTestDetail { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedByAdministratorId { get; set; }

    public Guid? UpdatedByUserId { get; set; }
}
