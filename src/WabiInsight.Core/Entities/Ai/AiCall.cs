namespace WabiInsight.Core.Entities.Ai;

/// <summary>
/// The AI usage ledger (P13.48): one row for every request that is sent to a model, written where the request
/// leaves the application, so no feature can call a model without being counted. Append-only. A row carries tokens,
/// the prices they were charged at and the resulting cost; never a prompt, an answer or a row of data.
/// The month's AI spending of an organization is the sum of <see cref="TotalCost"/> over its rows.
/// </summary>
public class AiCall
{
    public Guid CallId { get; set; }

    /// <summary>The organization the call was made for; null only for a Platform Administrator's provider test.</summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>The organization user the call was made for, when there is one.</summary>
    public Guid? UserId { get; set; }

    /// <summary>What the call was for: one of <see cref="AiCallPurposes"/>.</summary>
    public string Purpose { get; set; } = AiCallPurposes.Other;

    /// <summary>The question run the call belongs to (<see cref="AnalysisExecutionMetric.MetricId"/>), which leads to its history version; null for calls outside a question.</summary>
    public Guid? RunId { get; set; }

    public Guid? ProviderId { get; set; }

    public string ProviderType { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    /// <summary>All input tokens, including the cached ones read and written.</summary>
    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public long CachedInputTokens { get; set; }

    public long CacheWriteTokens { get; set; }

    /// <summary>Cost of the input tokens; null on rows copied from before the ledger, where only the total was kept.</summary>
    public decimal? InputCost { get; set; }

    /// <summary>Cost of the output tokens; null on rows copied from before the ledger.</summary>
    public decimal? OutputCost { get; set; }

    public decimal TotalCost { get; set; }

    /// <summary>The prices per million tokens the cost was calculated with; a later price change never rewrites a row.</summary>
    public decimal? InputPricePerMillion { get; set; }

    public decimal? OutputPricePerMillion { get; set; }

    public decimal? CachedInputPricePerMillion { get; set; }

    /// <summary>False when the provider did not answer; such a row has no tokens and costs nothing.</summary>
    public bool Succeeded { get; set; }

    public string? FailureCode { get; set; }

    public string? StopReason { get; set; }

    public double DurationMs { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Stable codes of what a model was called for.</summary>
public static class AiCallPurposes
{
    public const string Plan = "Plan";
    public const string Interpret = "Interpret";
    public const string Report = "Report";
    public const string RuleDraft = "RuleDraft";
    public const string ProviderTest = "ProviderTest";

    /// <summary>Rows copied from the per-question totals kept before the ledger: plan, interpretation and report together.</summary>
    public const string Analysis = "Analysis";

    /// <summary>A call whose caller did not say what it was for. It is still recorded and counted.</summary>
    public const string Other = "Other";
}
