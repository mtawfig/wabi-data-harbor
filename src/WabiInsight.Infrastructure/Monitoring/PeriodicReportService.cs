using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Analysis;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Analysis;
using WabiInsight.Core.Entities.Monitoring;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Monitoring;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Storage;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Monitoring;

/// <summary>
/// Periodic Reports (specification §35.3, owner decision D6). A subscription schedules a saved analysis the author
/// owns or that was shared with them (the owner's consent). Organization Administrators manage every subscription
/// and choose recipients among active users; Management users subscribe themselves only; everyone else receives.
/// Recipients always get figures computed under their own permissions (see <see cref="PeriodicReportRenderer"/>).
/// </summary>
public sealed class PeriodicReportService : IPeriodicReportService
{
    private static readonly string[] Languages = ["en", "ar", "recipient"];

    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly PeriodicReportRunner _runner;
    private readonly IArtifactStorage _storage;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;

    public PeriodicReportService(ConfigurationDbContext context, ITenantContext tenantContext, PeriodicReportRunner runner, IArtifactStorage storage, IAuditWriter audit, IClock clock)
    {
        _context = context;
        _tenantContext = tenantContext;
        _runner = runner;
        _storage = storage;
        _audit = audit;
        _clock = clock;
    }

    private Guid Self => _tenantContext.Principal.UserId ?? throw new TenantContextException("An organization user is required.");

    private bool IsAdmin => _tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin);

    private bool IsManagement => _tenantContext.Principal.Roles.Contains(SystemRoles.Management);

    public async Task<IReadOnlyList<SubscriptionAnalysisOption>> ListAnalysesAsync(Guid? forSubscriptionId = null, CancellationToken cancellationToken = default)
    {
        RequireAuthor();
        // A report is built on what its creator may use (their own analyses and the ones shared with them). An
        // administrator who edits someone else's report sees that person's choices, not their own.
        var creator = forSubscriptionId is { } id ? (await FindAsync(id, tracked: false, cancellationToken)).CreatedByUserId : Self;
        var histories = await EligibleHistories(creator).OrderByDescending(h => h.UpdatedAt ?? h.CreatedAt).Take(200).ToListAsync(cancellationToken);
        var ownerIds = histories.Select(h => h.OwnerUserId).Distinct().ToList();
        var owners = await _context.OrganizationUsers.AsNoTracking().Where(u => ownerIds.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.FullName, cancellationToken);
        var applicationIds = histories.Select(h => h.ApplicationId).Distinct().ToList();
        var applications = await _context.Applications.AsNoTracking().Where(a => applicationIds.Contains(a.ApplicationId)).ToDictionaryAsync(a => a.ApplicationId, cancellationToken);
        var historyIds = histories.Select(h => h.HistoryId).ToList();
        var panels = await _context.AnalysisHistoryVersions.AsNoTracking().Where(v => historyIds.Contains(v.HistoryId) && v.Status == AnalysisHistoryStatus.Completed)
            .GroupBy(v => v.HistoryId).Select(g => new { g.Key, Panels = g.OrderByDescending(v => v.VersionNumber).Select(v => v.PanelCount).First() }).ToDictionaryAsync(g => g.Key, g => g.Panels, cancellationToken);
        var fixedDates = await FixedDatesAsync(historyIds, cancellationToken);
        return histories.Where(h => panels.ContainsKey(h.HistoryId)).Select(h => new SubscriptionAnalysisOption(
            h.HistoryId, h.Title, applications.GetValueOrDefault(h.ApplicationId)?.ApplicationName ?? string.Empty, applications.GetValueOrDefault(h.ApplicationId)?.ApplicationNameAr ?? string.Empty,
            owners.GetValueOrDefault(h.OwnerUserId) ?? string.Empty, h.OwnerUserId == creator, h.CurrentVersionNumber, panels[h.HistoryId], h.UpdatedAt ?? h.CreatedAt,
            fixedDates.GetValueOrDefault(h.HistoryId) ?? [])).ToList();
    }

    /// <summary>Per analysis: the calendar dates written into its latest completed plan (empty when it follows the calendar).</summary>
    private async Task<Dictionary<Guid, IReadOnlyList<string>>> FixedDatesAsync(IReadOnlyCollection<Guid> historyIds, CancellationToken cancellationToken)
    {
        var plans = await _context.AnalysisHistoryVersions.AsNoTracking()
            .Where(v => historyIds.Contains(v.HistoryId) && v.Status == AnalysisHistoryStatus.Completed)
            .Select(v => new { v.HistoryId, v.VersionNumber, v.QueryPlanJson })
            .ToListAsync(cancellationToken);
        return plans.GroupBy(v => v.HistoryId).ToDictionary(g => g.Key, g => PlanDates.FixedDates(g.OrderByDescending(v => v.VersionNumber).First().QueryPlanJson));
    }

    public async Task<IReadOnlyList<ReportSubscriptionDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        RequireAuthor();
        var self = Self;
        var query = _context.ReportSubscriptions.AsNoTracking();
        if (!IsAdmin)
        {
            query = query.Where(s => s.CreatedByUserId == self);
        }

        return await MapAsync(await query.OrderBy(s => s.Name).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<ReportSubscriptionDto> GetAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        RequireAuthor();
        return (await MapAsync([await FindAsync(subscriptionId, tracked: false, cancellationToken)], cancellationToken))[0];
    }

    public async Task<ReportSubscriptionDto> CreateAsync(SaveReportSubscriptionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var organizationId = RequireAuthor();
        var recipients = await ValidateAsync(command, null, cancellationToken);
        var now = _clock.UtcNow;
        var subscription = new ReportSubscription { SubscriptionId = Ids.New(), OrganizationId = organizationId, CreatedByUserId = Self };
        Apply(subscription, command);
        _context.ReportSubscriptions.Add(subscription);
        foreach (var userId in recipients)
        {
            _context.ReportSubscriptionRecipients.Add(new ReportSubscriptionRecipient { RecipientId = Ids.New(), OrganizationId = organizationId, SubscriptionId = subscription.SubscriptionId, UserId = userId });
        }

        await SyncJobAsync(subscription, cancellationToken);
        _audit.Append(AuditEvents.SubscriptionSaved, "ReportSubscription", subscription.SubscriptionId.ToString(), new { created = true, subscription.HistoryId, recipients = recipients.Count, format = subscription.Format.ToString(), recurrence = subscription.RecurrenceKind.ToString() });
        await _context.SaveChangesAsync(cancellationToken);
        return await GetAsync(subscription.SubscriptionId, cancellationToken);
    }

    public async Task<ReportSubscriptionDto> UpdateAsync(Guid subscriptionId, SaveReportSubscriptionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var organizationId = RequireAuthor();
        var subscription = await FindAsync(subscriptionId, tracked: true, cancellationToken);
        var recipients = await ValidateAsync(command, subscription, cancellationToken);
        Apply(subscription, command);
        if (subscription.IsActive)
        {
            subscription.PausedReason = null;
            subscription.ConsecutiveFailures = 0;
        }

        _context.ReportSubscriptionRecipients.RemoveRange(await _context.ReportSubscriptionRecipients.Where(r => r.SubscriptionId == subscriptionId).ToListAsync(cancellationToken));
        foreach (var userId in recipients)
        {
            _context.ReportSubscriptionRecipients.Add(new ReportSubscriptionRecipient { RecipientId = Ids.New(), OrganizationId = organizationId, SubscriptionId = subscriptionId, UserId = userId });
        }

        await SyncJobAsync(subscription, cancellationToken);
        _audit.Append(AuditEvents.SubscriptionSaved, "ReportSubscription", subscriptionId.ToString(), new { created = false, subscription.HistoryId, recipients = recipients.Count, format = subscription.Format.ToString(), recurrence = subscription.RecurrenceKind.ToString(), subscription.IsActive });
        await _context.SaveChangesAsync(cancellationToken);
        return await GetAsync(subscriptionId, cancellationToken);
    }

    public async Task<ReportSubscriptionDto> SetActiveAsync(Guid subscriptionId, bool isActive, CancellationToken cancellationToken = default)
    {
        RequireAuthor();
        var subscription = await FindAsync(subscriptionId, tracked: true, cancellationToken);
        // Judged for the report's creator: an administrator can resume a report someone else set up (P13.27).
        if (isActive && !await EligibleHistories(subscription.CreatedByUserId).AnyAsync(h => h.HistoryId == subscription.HistoryId, cancellationToken))
        {
            throw Invalid("subscription_analysis_not_available");
        }

        subscription.IsActive = isActive;
        subscription.PausedReason = null;
        subscription.ConsecutiveFailures = 0;
        await SyncJobAsync(subscription, cancellationToken);
        _audit.Append(AuditEvents.SubscriptionActivation, "ReportSubscription", subscriptionId.ToString(), new { isActive });
        await _context.SaveChangesAsync(cancellationToken);
        return await GetAsync(subscriptionId, cancellationToken);
    }

    public async Task DeleteAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireAuthor();
        var subscription = await FindAsync(subscriptionId, tracked: true, cancellationToken);
        _context.ReportSubscriptionRecipients.RemoveRange(await _context.ReportSubscriptionRecipients.Where(r => r.SubscriptionId == subscriptionId).ToListAsync(cancellationToken));
        _context.ScheduledJobs.RemoveRange(await _context.ScheduledJobs.Where(j => j.OrganizationId == organizationId && j.JobType == ScheduledJobType.ReportSubscription && j.TargetId == subscriptionId).ToListAsync(cancellationToken));
        _context.ReportSubscriptions.Remove(subscription);
        // Delivered files and the log stay for the retention period; recipients keep their copies in My Reports.
        _audit.Append(AuditEvents.SubscriptionDeleted, "ReportSubscription", subscriptionId.ToString(), new { subscription.Name });
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<SubscriptionRunResultDto> RunNowAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        RequireAuthor();
        var subscription = await FindAsync(subscriptionId, tracked: false, cancellationToken);
        _audit.Append(AuditEvents.SubscriptionRunNow, "ReportSubscription", subscriptionId.ToString(), new { subscription.Name });
        var result = await _runner.RunAsync(subscription, manual: true, cancellationToken: cancellationToken);
        var names = await RecipientNamesAsync(result.Deliveries.Select(d => d.RecipientUserId), cancellationToken);
        var deliveries = result.Deliveries.Select(d => ToDto(d, names)).ToList();
        return new SubscriptionRunResultDto(result.RunId, deliveries.Count(d => d.Status == ReportDeliveryStatus.Delivered), deliveries.Count(d => d.Status == ReportDeliveryStatus.NoData),
            deliveries.Count(d => d.Status == ReportDeliveryStatus.Failed), result.ErrorCode, deliveries);
    }

    public async Task<PagedResult<ReportDeliveryDto>> ListDeliveriesAsync(Guid subscriptionId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        RequireAuthor();
        await FindAsync(subscriptionId, tracked: false, cancellationToken);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _context.ReportDeliveries.AsNoTracking().Where(d => d.SubscriptionId == subscriptionId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(d => d.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var names = await RecipientNamesAsync(items.Select(d => d.RecipientUserId), cancellationToken);
        return new PagedResult<ReportDeliveryDto>(items.Select(d => ToDto(d, names)).ToList(), page, pageSize, total);
    }

    public async Task<PagedResult<ReceivedReportDto>> ListReceivedAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        RequireUser();
        var self = Self;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _context.ReportDeliveries.AsNoTracking().Where(d => d.RecipientUserId == self && d.Status != ReportDeliveryStatus.Failed);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(d => d.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<ReceivedReportDto>(items.Select(d => new ReceivedReportDto(d.DeliveryId, d.SubscriptionId, d.ReportName, d.AnalysisTitle, d.Status, d.FileName, d.ContentType, d.SizeBytes, d.RowCount, d.Language, d.CreatedAt)).ToList(), page, pageSize, total);
    }

    public async Task<ArtifactDownload?> OpenReceivedAsync(Guid deliveryId, CancellationToken cancellationToken = default)
    {
        RequireUser();
        var self = Self;
        // Recipient only: not even the subscription's author or an administrator may open another person's figures.
        var delivery = await _context.ReportDeliveries.AsNoTracking().SingleOrDefaultAsync(d => d.DeliveryId == deliveryId && d.RecipientUserId == self, cancellationToken);
        if (delivery?.StorageKey is null)
        {
            return null;
        }

        var stream = await _storage.OpenReadAsync(delivery.StorageKey, cancellationToken);
        return stream is null ? null : new ArtifactDownload(stream, delivery.FileName ?? "report", delivery.ContentType ?? "application/octet-stream");
    }

    /// <summary>The analyses a user may build a report on: their own, and the ones shared with them (the owner's consent) while the share lasts.</summary>
    private IQueryable<AnalysisHistory> EligibleHistories(Guid self)
    {
        var now = _clock.UtcNow;
        return _context.AnalysisHistories.AsNoTracking().Where(h => !h.IsDeleted && (h.OwnerUserId == self
            || _context.AnalysisShares.Any(s => s.HistoryId == h.HistoryId && s.RecipientUserId == self && s.RevokedAt == null && (s.ExpiresAt == null || s.ExpiresAt > now))));
    }

    private async Task<List<Guid>> ValidateAsync(SaveReportSubscriptionCommand command, ReportSubscription? existing, CancellationToken cancellationToken)
    {
        var subscriptionId = existing?.SubscriptionId;
        var name = command.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 200)
        {
            throw Invalid("subscription_name_invalid");
        }

        if (await _context.ReportSubscriptions.AnyAsync(s => s.Name == name && s.SubscriptionId != subscriptionId, cancellationToken))
        {
            throw Invalid("subscription_name_taken");
        }

        var scheduleValid = command.RecurrenceKind switch
        {
            JobRecurrenceKind.Daily => true,
            JobRecurrenceKind.Weekly => command.DayOfWeek is >= 1 and <= 7,
            JobRecurrenceKind.Monthly => command.DayOfMonth is >= 1 and <= 31,
            _ => false,
        };
        if (!scheduleValid)
        {
            throw Invalid("subscription_schedule_invalid");
        }

        if (command.Format is not (ExportFormat.Pdf or ExportFormat.Xlsx))
        {
            throw Invalid("subscription_format_invalid");
        }

        if (!Languages.Contains(command.Language))
        {
            throw Invalid("subscription_language_invalid");
        }

        // The analysis must be the creator's own or shared with the creator (owner consent), in this organization. For
        // an existing report that is the person who set it up, whoever edits it now (P13.27): an administrator can
        // change another user's schedule, recipients or format, and pick among that user's analyses, not their own.
        if (!await EligibleHistories(existing?.CreatedByUserId ?? Self).AnyAsync(h => h.HistoryId == command.HistoryId, cancellationToken))
        {
            throw Invalid("subscription_analysis_not_available");
        }

        var recipients = (command.RecipientUserIds ?? []).Distinct().ToList();
        if (!IsAdmin)
        {
            // D6: Management users subscribe themselves only.
            if (recipients.Any(r => r != Self))
            {
                throw new TenantContextException("Management users can only subscribe themselves.");
            }

            return [Self];
        }

        if (recipients.Count is 0 or > 50)
        {
            throw Invalid("subscription_recipients_invalid");
        }

        if (await _context.OrganizationUsers.CountAsync(u => recipients.Contains(u.UserId) && u.Status == OrganizationUserStatus.Active, cancellationToken) != recipients.Count)
        {
            throw Invalid("subscription_recipients_invalid");
        }

        return recipients;
    }

    private static void Apply(ReportSubscription subscription, SaveReportSubscriptionCommand command)
    {
        subscription.Name = command.Name.Trim();
        subscription.HistoryId = command.HistoryId;
        subscription.RecurrenceKind = command.RecurrenceKind;
        subscription.TimeOfDay = new TimeOnly(command.TimeOfDay.Hour, command.TimeOfDay.Minute);
        subscription.DayOfWeek = command.RecurrenceKind == JobRecurrenceKind.Weekly ? command.DayOfWeek : null;
        subscription.DayOfMonth = command.RecurrenceKind == JobRecurrenceKind.Monthly ? command.DayOfMonth : null;
        subscription.Format = command.Format;
        subscription.Language = command.Language;
        subscription.IsActive = command.IsActive;
    }

    private async Task SyncJobAsync(ReportSubscription subscription, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var job = await _context.ScheduledJobs.FirstOrDefaultAsync(j => j.OrganizationId == subscription.OrganizationId && j.JobType == ScheduledJobType.ReportSubscription && j.TargetId == subscription.SubscriptionId, cancellationToken);
        if (job is null)
        {
            job = new ScheduledJob { JobId = Ids.New(), JobType = ScheduledJobType.ReportSubscription, OrganizationId = subscription.OrganizationId, TargetId = subscription.SubscriptionId, CreatedAt = now };
            _context.ScheduledJobs.Add(job);
        }

        var zone = await _context.Organizations.AsNoTracking().Where(o => o.OrganizationId == subscription.OrganizationId).Select(o => o.TimeZone).SingleAsync(cancellationToken);
        MonitoringSchedules.Apply(job, subscription.RecurrenceKind, 0, subscription.TimeOfDay, subscription.DayOfWeek, zone, subscription.IsActive, now, subscription.DayOfMonth);
    }

    private async Task<IReadOnlyList<ReportSubscriptionDto>> MapAsync(IReadOnlyList<ReportSubscription> subscriptions, CancellationToken cancellationToken)
    {
        var ids = subscriptions.Select(s => s.SubscriptionId).ToList();
        var recipients = await _context.ReportSubscriptionRecipients.AsNoTracking().Where(r => ids.Contains(r.SubscriptionId)).ToListAsync(cancellationToken);
        var userIds = recipients.Select(r => r.UserId).Concat(subscriptions.Select(s => s.CreatedByUserId)).Distinct().ToList();
        var users = await _context.OrganizationUsers.AsNoTracking().Where(u => userIds.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, cancellationToken);
        var historyIds = subscriptions.Select(s => s.HistoryId).Distinct().ToList();
        var titles = await _context.AnalysisHistories.AsNoTracking().Where(h => historyIds.Contains(h.HistoryId)).ToDictionaryAsync(h => h.HistoryId, h => h.Title, cancellationToken);
        var jobs = await _context.ScheduledJobs.AsNoTracking().Where(j => j.JobType == ScheduledJobType.ReportSubscription && j.TargetId != null && ids.Contains(j.TargetId.Value)).ToListAsync(cancellationToken);
        var lastRuns = await _context.ReportDeliveries.AsNoTracking().Where(d => ids.Contains(d.SubscriptionId))
            .GroupBy(d => new { d.SubscriptionId, d.RunId })
            .Select(g => new { g.Key.SubscriptionId, g.Key.RunId, At = g.Max(d => d.CreatedAt), Delivered = g.Count(d => d.Status == ReportDeliveryStatus.Delivered), NoData = g.Count(d => d.Status == ReportDeliveryStatus.NoData), Failed = g.Count(d => d.Status == ReportDeliveryStatus.Failed) })
            .ToListAsync(cancellationToken);
        var fixedDates = await FixedDatesAsync(historyIds, cancellationToken);
        return subscriptions.Select(s =>
        {
            var job = jobs.FirstOrDefault(j => j.TargetId == s.SubscriptionId);
            var last = lastRuns.Where(r => r.SubscriptionId == s.SubscriptionId).OrderByDescending(r => r.At).FirstOrDefault();
            return new ReportSubscriptionDto(
                s.SubscriptionId, s.Name, s.HistoryId, titles.GetValueOrDefault(s.HistoryId) ?? string.Empty, s.RecurrenceKind, s.TimeOfDay, s.DayOfWeek, s.DayOfMonth, s.Format, s.Language,
                s.IsActive, s.PausedReason, s.ConsecutiveFailures, s.LastRunAt, job is { IsEnabled: true } ? job.NextRunAt : null, s.CreatedByUserId,
                users.GetValueOrDefault(s.CreatedByUserId)?.FullName ?? string.Empty,
                recipients.Where(r => r.SubscriptionId == s.SubscriptionId).Select(r => users.TryGetValue(r.UserId, out var u) ? new SubscriptionRecipientDto(u.UserId, u.FullName, u.Email, u.Status.ToString()) : new SubscriptionRecipientDto(r.UserId, string.Empty, string.Empty, "Unknown")).ToList(),
                last is null ? null : new SubscriptionRunSummary(last.RunId, last.At, last.Delivered, last.NoData, last.Failed),
                CanEdit(s), s.CreatedAt, fixedDates.GetValueOrDefault(s.HistoryId) ?? []);
        }).ToList();
    }

    private async Task<Dictionary<Guid, string>> RecipientNamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        return await _context.OrganizationUsers.AsNoTracking().Where(u => ids.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.FullName, cancellationToken);
    }

    private static ReportDeliveryDto ToDto(ReportDelivery d, IReadOnlyDictionary<Guid, string> names) => new(
        d.DeliveryId, d.SubscriptionId, d.RunId, d.RecipientUserId, names.GetValueOrDefault(d.RecipientUserId) ?? string.Empty, d.Status, d.IsManualRun, d.FileName, d.SizeBytes, d.RowCount, d.Language, d.ErrorCode, d.CreatedAt);

    private bool CanEdit(ReportSubscription subscription) => IsAdmin || subscription.CreatedByUserId == Self;

    private async Task<ReportSubscription> FindAsync(Guid subscriptionId, bool tracked, CancellationToken cancellationToken)
    {
        var query = tracked ? _context.ReportSubscriptions : _context.ReportSubscriptions.AsNoTracking();
        var subscription = await query.SingleOrDefaultAsync(s => s.SubscriptionId == subscriptionId, cancellationToken);
        if (subscription is null || !(IsAdmin || subscription.CreatedByUserId == Self))
        {
            throw new DomainException(OrganizationOperationOutcome.NotFound, "subscription_not_found");
        }

        return subscription;
    }

    private Guid RequireUser()
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        if (_tenantContext.Principal.PrincipalType != PrincipalType.OrganizationUser)
        {
            throw new TenantContextException("Periodic reports belong to organization users.");
        }

        return organizationId;
    }

    private Guid RequireAuthor()
    {
        var organizationId = RequireUser();
        if (!(IsAdmin || IsManagement))
        {
            throw new TenantContextException("Periodic reports are managed by Organization Administrators and Management users.");
        }

        return organizationId;
    }

    private static DomainException Invalid(string code) => new(OrganizationOperationOutcome.ValidationFailed, code);
}
