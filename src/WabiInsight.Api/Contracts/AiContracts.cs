using System.ComponentModel.DataAnnotations;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Entities.Ai;

namespace WabiInsight.Api.Contracts;

public sealed record SaveAiProviderRequest(
    AiProviderType ProviderType,
    [Required, MaxLength(200)] string Name,
    [MaxLength(500)] string? Endpoint,
    [Required, MaxLength(200)] string Model,
    /// <summary>Write-only. Null or empty keeps the stored key.</summary>
    [MaxLength(1000)] string? ApiKey,
    [Range(0, 100000)] decimal InputPricePerMillion = 0,
    [Range(0, 100000)] decimal OutputPricePerMillion = 0,
    [Range(0, 100000)] decimal CachedInputPricePerMillion = 0,
    [Range(256, 65536)] int MaxOutputTokens = 4096,
    [RegularExpression("^(low|medium|high)$")] string? Effort = null,
    bool IsEnabled = true,
    bool IsApproved = false)
{
    public SaveAiProviderCommand ToCommand() => new(ProviderType, Name, Endpoint, Model, ApiKey, InputPricePerMillion, OutputPricePerMillion, CachedInputPricePerMillion, MaxOutputTokens, Effort, IsEnabled, IsApproved);
}

public sealed record SetAllowedProviderTypesRequest([Required] IReadOnlyCollection<AiProviderType> Types);

public sealed record SelectAiProviderRequest(Guid? ProviderId);

public sealed record PlanQuestionRequest([Required] Guid ApplicationId, [Required, MaxLength(2000)] string Question, [RegularExpression("^(en|ar)$")] string Language = "en");

/// <summary>Rejected plans are reported as 422 with the stage and stable codes, never as a generic failure.</summary>
public sealed record PlanRejectedResponse(string Stage, IReadOnlyList<PlanError> Errors);

public sealed record AskQuestionRequest([Required] Guid ApplicationId, [Required, MaxLength(2000)] string Question, [RegularExpression("^(en|ar)$")] string Language = "en", bool Interpret = true, [MaxLength(300)] string? Clarification = null, bool SkipClarification = false);

/// <summary>Execution failures (source unreachable, timeout, schema drift) carry a stable code for the UI.</summary>
public sealed record AnalysisFailedResponse(string Stage, string Code);
