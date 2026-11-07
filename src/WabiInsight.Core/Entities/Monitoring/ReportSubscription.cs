using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Analysis;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Monitoring;

public enum ReportDeliveryStatus
{
    /// <summary>The report was rendered under the recipient's permissions and emailed with the file.</summary>
    Delivered = 1,

    /// <summary>The recipient may not read the analysis's data: a short "no data for your permissions" email, no file.</summary>
    NoData = 2,

    Failed = 3,
}

/// <summary>
/// A Periodic Report (specification §35.3, owner decision D6): a saved analysis re-run on a schedule and emailed to
/// each recipient, rendered under <b>that recipient's</b> permissions. Organization Administrators manage any
/// subscription; Management users subscribe themselves only.
/// </summary>
public class ReportSubscription : AuditableEntity, ITenantOwned
{
    public Guid SubscriptionId { get; set; }

    public Guid OrganizationId { get; set; }

    /// <summary>The saved analysis whose current plan is re-run (never the stored figures, never the AI narrative).</summary>
    public Guid HistoryId { get; set; }

    public Guid CreatedByUserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public JobRecurrenceKind RecurrenceKind { get; set; } = JobRecurrenceKind.Weekly;

    public TimeOnly TimeOfDay { get; set; } = new(7, 0);

    /// <summary>ISO day (1 = Monday) for weekly reports.</summary>
    public int? DayOfWeek { get; set; }

    /// <summary>1–31 for monthly reports (31 means the last day of shorter months).</summary>
    public int? DayOfMonth { get; set; }

    /// <summary>Pdf or Xlsx.</summary>
    public ExportFormat Format { get; set; } = ExportFormat.Pdf;

    /// <summary>"en", "ar", or "recipient" (each recipient's preferred language).</summary>
    public string Language { get; set; } = "recipient";

    public bool IsActive { get; set; } = true;

    /// <summary>Why the subscription was paused automatically (three failed runs in a row); null when paused by a person or active.</summary>
    public string? PausedReason { get; set; }

    public int ConsecutiveFailures { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }
}

public class ReportSubscriptionRecipient : ITenantOwned
{
    public Guid RecipientId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid SubscriptionId { get; set; }

    public Guid UserId { get; set; }
}

/// <summary>One row per recipient per run (the delivery log). Written once; the file itself lives in artifact storage.</summary>
public class ReportDelivery : ITenantOwned
{
    public Guid DeliveryId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid SubscriptionId { get; set; }

    /// <summary>Groups the deliveries of one run.</summary>
    public Guid RunId { get; set; }

    public Guid RecipientUserId { get; set; }

    public ReportDeliveryStatus Status { get; set; }

    /// <summary>The subscription's name at delivery time (kept when the subscription is deleted).</summary>
    public string ReportName { get; set; } = string.Empty;

    /// <summary>The analysis title at delivery time.</summary>
    public string AnalysisTitle { get; set; } = string.Empty;

    public bool IsManualRun { get; set; }

    public string? StorageKey { get; set; }

    public string? FileName { get; set; }

    public string? ContentType { get; set; }

    public long SizeBytes { get; set; }

    public int RowCount { get; set; }

    public string Language { get; set; } = "en";

    public Guid? EmailMessageId { get; set; }

    public string? ErrorCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
