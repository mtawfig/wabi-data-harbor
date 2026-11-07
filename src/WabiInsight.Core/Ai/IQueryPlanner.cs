namespace WabiInsight.Core.Ai;

public sealed record PlanRequest(Guid ApplicationId, string Question, string Language);

public sealed record PlanUsage(string Provider, string Model, long InputTokens, long OutputTokens, long CachedInputTokens, decimal EstimatedCost, double DurationMs);

/// <summary>Validated and authorized answer plan ready for the compiler: one authorized plan per query, in order. Nothing here has touched the business database.</summary>
public sealed record PlanResponse(AnalysisPlan Plan, IReadOnlyList<AuthorizedPlan> Authorized, PlanUsage Usage, Entities.Metadata.DataClassification Classification = Entities.Metadata.DataClassification.Internal);

/// <summary>Thrown when the model's plan is rejected: shape errors or authorization errors, with stable codes for the UI.</summary>
public sealed class PlanRejectedException : Exception
{
    public PlanRejectedException(string stage, IReadOnlyList<PlanError> errors)
        : base($"{stage}: {string.Join("; ", errors.Select(e => e.Code))}")
    {
        Stage = stage;
        Errors = errors;
    }

    public PlanRejectedException()
    {
        Stage = "unknown";
        Errors = [];
    }

    public PlanRejectedException(string message)
        : base(message)
    {
        Stage = "unknown";
        Errors = [];
    }

    public PlanRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
        Stage = "unknown";
        Errors = [];
    }

    /// <summary>"shape", "authorization" or "format".</summary>
    public string Stage { get; }

    /// <summary>The parsed plan when rejection happened after parsing (shape or authorization), so the access ledger can record what was attempted.</summary>
    public AnalysisPlan? Plan { get; init; }

    public IReadOnlyList<PlanError> Errors { get; }
}

/// <summary>Steps 1-10 of the secure workflow (specification §19): authorized snapshot, provider call, validation, authorization.</summary>
public interface IQueryPlanner
{
    Task<PlanResponse> PlanAsync(PlanRequest request, CancellationToken cancellationToken = default);

    /// <summary>The snapshot the planner would send for an application: what this user may ask about.</summary>
    Task<MetadataSnapshot> GetSnapshotAsync(Guid applicationId, CancellationToken cancellationToken = default);

    /// <summary>Permitted-object counts per askable application for the current user, from one permission resolution (P12.2: the application list must not build a full snapshot per application).</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountPermittedObjectsAsync(CancellationToken cancellationToken = default);
}
