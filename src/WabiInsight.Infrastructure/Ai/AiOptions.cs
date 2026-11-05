namespace WabiInsight.Infrastructure.Ai;

/// <summary>Bound from the "Ai" section. The Anthropic key seeds the platform default provider row on startup (owner decision: real values in appsettings).</summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public AnthropicOptions Anthropic { get; set; } = new();

    public string DefaultEffort { get; set; } = "medium";

    public int MaxOutputTokens { get; set; } = 4096;

    public int RequestTimeoutSeconds { get; set; } = 90;

    /// <summary>Statement timeout for analysis queries on organization sources.</summary>
    public int QueryTimeoutSeconds { get; set; } = 30;

    /// <summary>Default allow-list of provider types organizations may configure themselves.</summary>
    public string[] AllowedProviderTypes { get; set; } = ["Anthropic", "OpenAiCompatible", "Gemini"];

    public sealed class AnthropicOptions
    {
        public string? ApiKey { get; set; }

        public string Model { get; set; } = "claude-opus-5-5";

        public decimal InputPricePerMillion { get; set; } = 4m;

        public decimal OutputPricePerMillion { get; set; } = 20m;

        public decimal CachedInputPricePerMillion { get; set; } = 0.20m;
    }
}
