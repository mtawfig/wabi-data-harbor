using WabiInsight.Core.Entities.Ai;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Analysis;

/// <summary>
/// One row per question asked, whatever its outcome: what was asked, the validated plan, and the read-only SQL each panel ran
/// with its bound parameter values (filter literals and the asker's own codes) and row count. It exists for reference, debugging and checking the tool's accuracy against
/// the request; it is never shown to users or returned by an endpoint. Append-only.
/// </summary>
public class AnalysisQueryLog : ITenantOwned
{
    public Guid LogId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid UserId { get; set; }

    public Guid ApplicationId { get; set; }

    /// <summary>The <see cref="AnalysisExecutionMetric"/> of the same run.</summary>
    public Guid MetricId { get; set; }

    /// <summary>The saved analysis and version, when the run produced or re-ran one.</summary>
    public Guid? HistoryId { get; set; }

    public int? VersionNumber { get; set; }

    public string Question { get; set; } = string.Empty;

    public string Language { get; set; } = "en";

    public AnalysisStatus Status { get; set; }

    public string? FailureStage { get; set; }

    public string? FailureCode { get; set; }

    /// <summary>The analysis plan as validated (empty object when planning failed before a plan existed).</summary>
    public string PlanJson { get; set; } = "{}";

    /// <summary>JSON array of <c>{ title, sql, parameters: [{ name, value }], rowCount, truncated }</c>, one per compiled panel: the SQL shows each value as "?" and <c>parameters</c> lists the bound values in order.</summary>
    public string QueriesJson { get; set; } = "[]";

    public DateTimeOffset CreatedAt { get; set; }
}
