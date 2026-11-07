using WabiInsight.Core.Entities.Analysis;
using WabiInsight.Core.Entities.Monitoring;

namespace WabiInsight.Core.Monitoring;

/// <summary>Everything the ledger needs about one analysis, successful or not.</summary>
public sealed record AccessFactRecording(
    Guid ApplicationId,
    IReadOnlyList<PlanExecution> Queries,
    AccessOutcome Outcome,
    Guid? HistoryVersionId = null,
    Guid? MetricId = null,
    string? FailureCode = null,
    bool IsExport = false,
    ExportFormat? ExportFormat = null);

/// <summary>
/// Writes <see cref="AnalysisAccessFact"/> rows for the current organization user into the ambient unit of work
/// (the caller's SaveChanges persists them together with the analysis). Never saves by itself and never reads
/// business data: it maps plan references to the organization's catalogue only.
/// </summary>
public interface IAccessFactRecorder
{
    Task RecordAsync(AccessFactRecording recording, CancellationToken cancellationToken = default);
}

/// <summary>Settings of the Phase 14 scheduler and ledger (section "Monitoring").</summary>
public sealed class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    /// <summary>Runs the background scheduler in this process. Off in tests, which drive the runner directly.</summary>
    public bool SchedulerEnabled { get; set; } = true;

    public int SchedulerTickSeconds { get; set; } = 60;

    /// <summary>How long a job stays leased to one instance; a crashed instance's jobs become available again after this.</summary>
    public int LeaseMinutes { get; set; } = 10;

    /// <summary>Default retention for new organizations (owner decision D3: 24 months).</summary>
    public int DefaultRetentionMonths { get; set; } = 24;

    /// <summary>Platform maximum an organization may configure.</summary>
    public int MaxRetentionMonths { get; set; } = 60;

    public int BaselineWindowDays { get; set; } = 28;
}
