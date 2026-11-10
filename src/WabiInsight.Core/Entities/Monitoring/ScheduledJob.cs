namespace WabiInsight.Core.Entities.Monitoring;

public enum ScheduledJobType
{
    /// <summary>Nightly per-organization baselines over the access ledger.</summary>
    AccessBaselines = 1,

    /// <summary>Nightly per-organization purge of ledger rows older than the organization's retention.</summary>
    RetentionPurge = 2,

    /// <summary>Daily alert digest (P14.2).</summary>
    AlertDigest = 3,

    /// <summary>A scheduled monitoring rule (P14.2); <see cref="ScheduledJob.TargetId"/> is the rule.</summary>
    MonitoringRule = 4,

    /// <summary>A periodic report subscription (P14.3); <see cref="ScheduledJob.TargetId"/> is the subscription.</summary>
    ReportSubscription = 5,
}

public enum JobRecurrenceKind
{
    /// <summary>Every <see cref="ScheduledJob.IntervalMinutes"/> minutes.</summary>
    Interval = 1,

    /// <summary>Every day at <see cref="ScheduledJob.TimeOfDay"/> in <see cref="ScheduledJob.TimeZone"/>.</summary>
    Daily = 2,

    /// <summary>Every week on <see cref="ScheduledJob.DayOfWeek"/> (1 = Monday … 7 = Sunday).</summary>
    Weekly = 3,

    /// <summary>Every month on <see cref="ScheduledJob.DayOfMonth"/> (clamped to the month's length).</summary>
    Monthly = 4,
}

public enum JobOutcome
{
    Succeeded = 1,
    Failed = 2,
}

/// <summary>
/// One schedulable unit of background work (Phase 14, specification §35.4). The scheduler takes due rows with an
/// atomic lease, so several API instances never run the same job twice, and computes the next run strictly after
/// now, so missed runs (the API was down) execute once, not once per missed slot. Platform-wide jobs have no
/// organization; organization jobs run with explicit organization scoping, never through a tenant principal.
/// </summary>
public class ScheduledJob
{
    public Guid JobId { get; set; }

    public ScheduledJobType JobType { get; set; }

    public Guid? OrganizationId { get; set; }

    /// <summary>The rule or subscription the job runs, when the type has one.</summary>
    public Guid? TargetId { get; set; }

    public JobRecurrenceKind RecurrenceKind { get; set; } = JobRecurrenceKind.Daily;

    public int IntervalMinutes { get; set; }

    public TimeOnly TimeOfDay { get; set; }

    public int? DayOfWeek { get; set; }

    public int? DayOfMonth { get; set; }

    /// <summary>IANA or Windows time-zone id of the organization; UTC for platform jobs.</summary>
    public string TimeZone { get; set; } = "UTC";

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset NextRunAt { get; set; }

    public string? LeaseOwner { get; set; }

    public DateTimeOffset? LeaseUntil { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>
    /// When the job last finished without an error (P13.27). A job that works on "everything since last time" (the
    /// alert digest) counts from here, so a failed run or a retry never skips a period.
    /// </summary>
    public DateTimeOffset? LastSuccessAt { get; set; }

    public JobOutcome? LastOutcome { get; set; }

    public string? LastError { get; set; }

    public double LastDurationMs { get; set; }

    public int ConsecutiveFailures { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
