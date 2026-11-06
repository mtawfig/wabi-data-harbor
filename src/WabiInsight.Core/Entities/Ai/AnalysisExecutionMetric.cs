using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Ai;

public enum AnalysisStatus
{
    Completed = 1,
    Rejected = 2,
    Failed = 3,
}

/// <summary>One row per analysis attempt (specification data model): timings, tokens, cost and outcome. Never the question text or rows.</summary>
public class AnalysisExecutionMetric : ITenantOwned
{
    public Guid MetricId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid UserId { get; set; }

    public Guid ApplicationId { get; set; }

    public Guid? ProviderId { get; set; }

    public string? Model { get; set; }

    public AnalysisStatus Status { get; set; }

    public string? FailureStage { get; set; }

    public string? FailureCode { get; set; }

    public long PlanInputTokens { get; set; }

    public long PlanOutputTokens { get; set; }

    public long InterpretInputTokens { get; set; }

    public long InterpretOutputTokens { get; set; }

    public decimal EstimatedCost { get; set; }

    public double PlanMs { get; set; }

    public double ExecuteMs { get; set; }

    public double InterpretMs { get; set; }

    public int RowCount { get; set; }

    public bool Truncated { get; set; }

    public int ObjectCount { get; set; }

    public string Language { get; set; } = "en";

    /// <summary>Storage key of the rendered PDF when the answer included a report; downloadable by the user who ran the analysis.</summary>
    public string? ReportArtifactKey { get; set; }

    public string? ReportFileName { get; set; }

    public long? ReportSizeBytes { get; set; }

    public long ReportInputTokens { get; set; }

    public long ReportOutputTokens { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
