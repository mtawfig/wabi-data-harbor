using WabiInsight.Core.Ai.Compilers;

namespace WabiInsight.Core.Ai;

/// <summary>Executes a compiled, read-only query against one application's source with a statement timeout and row cap.</summary>
public interface IQueryExecutor
{
    Task<AnalysisResult> ExecuteAsync(Guid applicationId, CompiledQuery query, PlanVisualization? visualization, CancellationToken cancellationToken = default);
}

public sealed record Interpretation(string Narrative, IReadOnlyList<string> Highlights, PlanUsage Usage);

/// <summary>One application the signed-in user may ask about.</summary>
public sealed record AskableApplication(Guid ApplicationId, string ApplicationCode, string ApplicationName, string ApplicationNameAr, string DatabaseType, bool IsBuiltIn, int ObjectCount, IReadOnlyList<Applications.ApplicationExampleQuestion> ExampleQuestions);

/// <summary>HistoryId set means "re-run this saved analysis": the outcome becomes its next version instead of a new history entry.</summary>
public sealed record AskRequest(Guid ApplicationId, string Question, string Language, bool Interpret = true, Guid? HistoryId = null);

/// <summary>
/// One executed query of the answer: a KPI, a chart or a table, with its own plan, redacted SQL and verified rows.
/// <see cref="RowScopes"/> lists the row scopes that limited its rows to what the asker may see (empty or null: every row of the
/// queried objects), so the interpretation and the page never present a permission-filtered result as the whole organization.
/// </summary>
public sealed record AnalysisPanel(string Title, QueryPlan Plan, string CompiledSqlPreview, AnalysisResult Result, IReadOnlyList<PanelRowScope>? RowScopes = null);

/// <summary>A row scope applied to a panel (<c>Self</c>, <c>Department</c> or <c>Section</c>) and the asker's own value it was bound to.</summary>
public sealed record PanelRowScope(string Scope, string Value);

/// <summary>Turns the authorized plan's row predicates into the panel's row scopes, and describes them for the model.</summary>
public static class PanelAccess
{
    public static IReadOnlyList<PanelRowScope> From(AuthorizedPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Objects.SelectMany(o => o.RowPredicates)
            .Where(p => p.Scope is not null)
            .Select(p => new PanelRowScope(p.Scope!.Value.ToString(), p.Value))
            .Distinct()
            .OrderBy(s => s.Scope, StringComparer.Ordinal).ThenBy(s => s.Value, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The access line given to the interpreter and the report writer for one panel.</summary>
    public static string Describe(IReadOnlyList<PanelRowScope>? scopes) => scopes is null || scopes.Count == 0
        ? "unrestricted: every row of the queried objects"
        : "limited to what the asker may see: " + string.Join(" or ", scopes.Select(s => s.Scope switch
        {
            "Self" => $"only the asker's own records (employee {s.Value})",
            "Department" => $"only rows of the asker's department or branch {s.Value}",
            "Section" => $"only rows of the asker's section {s.Value}",
            _ => $"only rows for {s.Scope} {s.Value}",
        }));
}

public sealed record AskResponse(
    Guid AnalysisId,
    Guid? HistoryId,
    int VersionNumber,
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
