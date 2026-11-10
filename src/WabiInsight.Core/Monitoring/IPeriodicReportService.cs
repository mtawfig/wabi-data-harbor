using WabiInsight.Core.Analysis;
using WabiInsight.Core.Entities.Analysis;
using WabiInsight.Core.Entities.Monitoring;
using WabiInsight.Core.Organizations;

namespace WabiInsight.Core.Monitoring;

/// <summary>A saved analysis the caller may schedule: their own, or one shared with them.</summary>
/// <param name="FixedDates">Calendar dates written into the analysis (yyyy-MM-dd). Not empty: every run covers the same period.</param>
public sealed record SubscriptionAnalysisOption(Guid HistoryId, string Title, string ApplicationName, string ApplicationNameAr, string OwnerName, bool IsOwn, int CurrentVersionNumber, int PanelCount, DateTimeOffset UpdatedAt, IReadOnlyList<string> FixedDates);

public sealed record SubscriptionRecipientDto(Guid UserId, string FullName, string Email, string Status);

public sealed record SubscriptionRunSummary(Guid RunId, DateTimeOffset RunAt, int Delivered, int NoData, int Failed);

public sealed record ReportSubscriptionDto(
    Guid SubscriptionId,
    string Name,
    Guid HistoryId,
    string HistoryTitle,
    JobRecurrenceKind RecurrenceKind,
    TimeOnly TimeOfDay,
    int? DayOfWeek,
    int? DayOfMonth,
    ExportFormat Format,
    string Language,
    bool IsActive,
    string? PausedReason,
    int ConsecutiveFailures,
    DateTimeOffset? LastRunAt,
    DateTimeOffset? NextRunAt,
    Guid CreatedByUserId,
    string CreatedByName,
    IReadOnlyList<SubscriptionRecipientDto> Recipients,
    SubscriptionRunSummary? LastRun,
    bool CanEdit,
    DateTimeOffset CreatedAt,
    /// <summary>Calendar dates written into the analysis (yyyy-MM-dd). Not empty: every run covers the same period.</summary>
    IReadOnlyList<string> FixedDates);

public sealed record SaveReportSubscriptionCommand(
    string Name,
    Guid HistoryId,
    JobRecurrenceKind RecurrenceKind,
    TimeOnly TimeOfDay,
    int? DayOfWeek,
    int? DayOfMonth,
    ExportFormat Format,
    string Language,
    bool IsActive,
    IReadOnlyList<Guid>? RecipientUserIds);

public sealed record ReportDeliveryDto(
    Guid DeliveryId,
    Guid SubscriptionId,
    Guid RunId,
    Guid RecipientUserId,
    string RecipientName,
    ReportDeliveryStatus Status,
    bool IsManualRun,
    string? FileName,
    long SizeBytes,
    int RowCount,
    string Language,
    string? ErrorCode,
    DateTimeOffset CreatedAt);

public sealed record SubscriptionRunResultDto(Guid RunId, int Delivered, int NoData, int Failed, string? ErrorCode, IReadOnlyList<ReportDeliveryDto> Deliveries);

/// <summary>A periodic report delivered to the caller ("sent to me" in My Reports).</summary>
public sealed record ReceivedReportDto(Guid DeliveryId, Guid SubscriptionId, string SubscriptionName, string HistoryTitle, ReportDeliveryStatus Status, string? FileName, string? ContentType, long SizeBytes, int RowCount, string Language, DateTimeOffset CreatedAt);

/// <summary>
/// Periodic Reports (specification §35.3, owner decision D6). Organization Administrators manage every subscription
/// and choose recipients among active users; Management users subscribe themselves only; everyone receives.
/// </summary>
public interface IPeriodicReportService
{
    /// <param name="forSubscriptionId">When editing a report: the analyses its <b>creator</b> may use (an administrator editing someone else's report keeps that person's choice).</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<IReadOnlyList<SubscriptionAnalysisOption>> ListAnalysesAsync(Guid? forSubscriptionId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReportSubscriptionDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<ReportSubscriptionDto> GetAsync(Guid subscriptionId, CancellationToken cancellationToken = default);

    Task<ReportSubscriptionDto> CreateAsync(SaveReportSubscriptionCommand command, CancellationToken cancellationToken = default);

    Task<ReportSubscriptionDto> UpdateAsync(Guid subscriptionId, SaveReportSubscriptionCommand command, CancellationToken cancellationToken = default);

    Task<ReportSubscriptionDto> SetActiveAsync(Guid subscriptionId, bool isActive, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Delivers now to every recipient (each under their own permissions); does not move the schedule.</summary>
    Task<SubscriptionRunResultDto> RunNowAsync(Guid subscriptionId, CancellationToken cancellationToken = default);

    Task<PagedResult<ReportDeliveryDto>> ListDeliveriesAsync(Guid subscriptionId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<PagedResult<ReceivedReportDto>> ListReceivedAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>The delivered file, for its recipient only.</summary>
    Task<ArtifactDownload?> OpenReceivedAsync(Guid deliveryId, CancellationToken cancellationToken = default);
}
