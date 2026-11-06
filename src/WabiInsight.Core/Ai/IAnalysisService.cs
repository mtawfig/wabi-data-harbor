using WabiInsight.Core.Ai.Compilers;

namespace WabiInsight.Core.Ai;

/// <summary>Executes a compiled, read-only query against one application's source with a statement timeout and row cap.</summary>
public interface IQueryExecutor
{
    Task<AnalysisResult> ExecuteAsync(Guid applicationId, CompiledQuery query, PlanVisualization? visualization, CancellationToken cancellationToken = default);
}

public sealed record Interpretation(string Narrative, IReadOnlyList<string> Highlights, PlanUsage Usage);

/// <summary>One application the signed-in user may ask about.</summary>
public sealed record AskableApplication(Guid ApplicationId, string ApplicationCode, string ApplicationName, string ApplicationNameAr, string DatabaseType, bool IsBuiltIn, int ObjectCount);

public sealed record AskRequest(Guid ApplicationId, string Question, string Language, bool Interpret = true);

/// <summary>One executed query of the answer: a KPI, a chart or a table, with its own plan, redacted SQL and verified rows.</summary>
public sealed record AnalysisPanel(string Title, QueryPlan Plan, string CompiledSqlPreview, AnalysisResult Result);

public sealed record AskResponse(
    Guid AnalysisId,
    string Objective,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<AnalysisPanel> Panels,
    Interpretation? Interpretation,
    GeneratedReport? Report,
    EmailDraft? EmailDraft,
    PlanUsage PlanUsage,
    decimal TotalEstimatedCost,
    double TotalDurationMs);

/// <summary>Steps 1-16 of the secure workflow (specification §19), without persistence (P9.1).</summary>
public interface IAnalysisService
{
    Task<IReadOnlyList<AskableApplication>> ListApplicationsAsync(CancellationToken cancellationToken = default);

    Task<AskResponse> AskAsync(AskRequest request, CancellationToken cancellationToken = default);

    /// <summary>The PDF of an analysis the signed-in user ran in their organization; null when there is none or it is not theirs.</summary>
    Task<ReportDownload?> OpenReportAsync(Guid analysisId, CancellationToken cancellationToken = default);
}
