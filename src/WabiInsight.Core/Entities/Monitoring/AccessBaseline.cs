using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Monitoring;

public enum BaselineSubjectKind
{
    User = 1,
    Role = 2,
}

/// <summary>
/// What is "normal" for one user, or for all users holding one role, over a rolling window of daily activity
/// (Phase 14, specification §35.1). Recomputed nightly from <see cref="AnalysisAccessFact"/>; one row per subject.
/// Percentiles are over the subject's active days (days with at least one fact); <see cref="ActiveDays"/> lets a
/// rule decide whether the baseline is mature enough to compare against.
/// </summary>
public class AccessBaseline : ITenantOwned
{
    public Guid BaselineId { get; set; }

    public Guid OrganizationId { get; set; }

    public BaselineSubjectKind SubjectKind { get; set; }

    public Guid? UserId { get; set; }

    public string? RoleCode { get; set; }

    public int WindowDays { get; set; } = 28;

    /// <summary>Last day (exclusive end at midnight UTC) covered by the window.</summary>
    public DateOnly WindowEnd { get; set; }

    public int ActiveDays { get; set; }

    public double QueriesPerDayMedian { get; set; }

    public double QueriesPerDayP95 { get; set; }

    public double RowsPerDayMedian { get; set; }

    public double RowsPerDayP95 { get; set; }

    public double RestrictedTouchesPerDayMedian { get; set; }

    public double RestrictedTouchesPerDayP95 { get; set; }

    public double ExportsPerDayMedian { get; set; }

    public double ExportsPerDayP95 { get; set; }

    public double UnauthorizedPerDayMedian { get; set; }

    public double UnauthorizedPerDayP95 { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}
