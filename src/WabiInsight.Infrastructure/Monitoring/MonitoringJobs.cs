using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Monitoring;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Monitoring;
using WabiInsight.Core.Tenancy;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Monitoring;

/// <summary>
/// Nightly baselines for one organization: median and p95 per day of queries, rows, restricted-column touches,
/// exports and unauthorized attempts, per user and per role, over the configured window. Reads the ledger with
/// explicit organization scope (the scheduler principal is platform-scope) and upserts one row per subject.
/// </summary>
public sealed class AccessBaselineJob : IScheduledJobHandler
{
    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IUsageEventWriter _usage;
    private readonly MonitoringOptions _options;

    public AccessBaselineJob(ConfigurationDbContext context, ITenantContext tenantContext, IClock clock, IUsageEventWriter usage, IOptions<MonitoringOptions> options)
    {
        _context = context;
        _tenantContext = tenantContext;
        _clock = clock;
        _usage = usage;
        _options = options.Value;
    }

    public ScheduledJobType JobType => ScheduledJobType.AccessBaselines;

    public async Task ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var organizationId = job.OrganizationId ?? throw new InvalidOperationException("Baselines are per organization.");
        var now = _clock.UtcNow;
        var windowDays = Math.Clamp(_options.BaselineWindowDays, 7, 90);
        var windowEnd = DateOnly.FromDateTime(now.UtcDateTime);
        var end = new DateTimeOffset(windowEnd.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var start = end.AddDays(-windowDays);

        var facts = await _context.AnalysisAccessFacts.AsPlatformScope(_tenantContext).AsNoTracking()
            .Where(f => f.OrganizationId == organizationId && f.AccessedAt >= start && f.AccessedAt < end)
            .Select(f => new { f.UserId, f.AccessedAt, f.IsPrimaryObject, f.MetricId, f.HistoryVersionId, f.RowCount, f.HighestClassification, f.IsExport, f.Outcome, f.RoleCodes })
            .ToListAsync(cancellationToken);

        // One activity record per user-day; queries are counted once per analysis (metric or version), not per object.
        var userDays = facts
            .GroupBy(f => (f.UserId, Day: DateOnly.FromDateTime(f.AccessedAt.UtcDateTime)))
            .Select(g => new
            {
                g.Key.UserId,
                Roles = g.Select(f => f.RoleCodes).Where(r => r.Length > 0).OrderByDescending(r => r.Length).FirstOrDefault() ?? string.Empty,
                Activity = new DailyActivity(
                    g.Where(f => f.Outcome == AccessOutcome.Succeeded && f.IsPrimaryObject).Select(f => f.MetricId ?? f.HistoryVersionId ?? Guid.Empty).Distinct().Count(),
                    g.Where(f => f.Outcome == AccessOutcome.Succeeded && f.IsPrimaryObject).Sum(f => (long)f.RowCount),
                    g.Count(f => f.Outcome == AccessOutcome.Succeeded && f.HighestClassification == DataClassification.Restricted),
                    g.Where(f => f.IsExport && f.IsPrimaryObject).Select(f => f.HistoryVersionId ?? Guid.Empty).Distinct().Count(),
                    g.Where(f => f.Outcome == AccessOutcome.Unauthorized && f.IsPrimaryObject).Select(f => f.MetricId ?? Guid.Empty).Distinct().Count()),
            })
            .ToList();

        var existing = await _context.AccessBaselines.AsPlatformScope(_tenantContext).Where(b => b.OrganizationId == organizationId).ToListAsync(cancellationToken);
        var touched = new HashSet<Guid>();

        foreach (var user in userDays.GroupBy(d => d.UserId))
        {
            var summary = BaselineStatistics.Summarize(user.Select(d => d.Activity).ToList());
            Upsert(existing, touched, organizationId, BaselineSubjectKind.User, user.Key, null, summary, windowDays, windowEnd, now);
        }

        var roleCodes = userDays.SelectMany(d => d.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Distinct(StringComparer.Ordinal).ToList();
        foreach (var role in roleCodes)
        {
            var days = userDays.Where(d => d.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(role, StringComparer.Ordinal)).Select(d => d.Activity).ToList();
            Upsert(existing, touched, organizationId, BaselineSubjectKind.Role, null, role, BaselineStatistics.Summarize(days), windowDays, windowEnd, now);
        }

        // Subjects without activity in the window keep their last baseline but are marked stale by WindowEnd staying old.
        _usage.Append(UsageEventTypes.BaselinesComputed, "Organization", organizationId.ToString(), quantity: touched.Count,
            metadata: new { users = userDays.Select(d => d.UserId).Distinct().Count(), roles = roleCodes.Count, windowDays, facts = facts.Count }, organizationId: organizationId);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private void Upsert(List<AccessBaseline> existing, HashSet<Guid> touched, Guid organizationId, BaselineSubjectKind kind, Guid? userId, string? roleCode, BaselineSummary summary, int windowDays, DateOnly windowEnd, DateTimeOffset now)
    {
        var row = existing.FirstOrDefault(b => b.SubjectKind == kind && b.UserId == userId && string.Equals(b.RoleCode, roleCode, StringComparison.Ordinal));
        if (row is null)
        {
            row = new AccessBaseline { BaselineId = Ids.New(), OrganizationId = organizationId, SubjectKind = kind, UserId = userId, RoleCode = roleCode };
            _context.AccessBaselines.Add(row);
            existing.Add(row);
        }

        row.WindowDays = windowDays;
        row.WindowEnd = windowEnd;
        row.ActiveDays = summary.ActiveDays;
        row.QueriesPerDayMedian = summary.QueriesMedian;
        row.QueriesPerDayP95 = summary.QueriesP95;
        row.RowsPerDayMedian = summary.RowsMedian;
        row.RowsPerDayP95 = summary.RowsP95;
        row.RestrictedTouchesPerDayMedian = summary.RestrictedMedian;
        row.RestrictedTouchesPerDayP95 = summary.RestrictedP95;
        row.ExportsPerDayMedian = summary.ExportsMedian;
        row.ExportsPerDayP95 = summary.ExportsP95;
        row.UnauthorizedPerDayMedian = summary.UnauthorizedMedian;
        row.UnauthorizedPerDayP95 = summary.UnauthorizedP95;
        row.ComputedAt = now;
        touched.Add(row.BaselineId);
    }
}

/// <summary>
/// Nightly purge of ledger rows older than the organization's retention (owner decision D3: 24 months by default,
/// never above the platform maximum). Uses a set-based delete, which is the only sanctioned way to remove rows from
/// the append-only ledger.
/// </summary>
public sealed class RetentionPurgeJob : IScheduledJobHandler
{
    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IUsageEventWriter _usage;
    private readonly MonitoringOptions _options;

    public RetentionPurgeJob(ConfigurationDbContext context, ITenantContext tenantContext, IClock clock, IUsageEventWriter usage, IOptions<MonitoringOptions> options, Core.Storage.IArtifactStorage storage)
    {
        _storage = storage;
        _context = context;
        _tenantContext = tenantContext;
        _clock = clock;
        _usage = usage;
        _options = options.Value;
    }

    private readonly Core.Storage.IArtifactStorage _storage;

    public ScheduledJobType JobType => ScheduledJobType.RetentionPurge;

    public async Task ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var organizationId = job.OrganizationId ?? throw new InvalidOperationException("Retention is per organization.");
        var organization = await _context.Organizations.AsNoTracking().SingleAsync(o => o.OrganizationId == organizationId, cancellationToken);
        var months = EffectiveRetentionMonths(organization, _options);
        var cutoff = _clock.UtcNow.AddMonths(-months);

        var deleted = await _context.AnalysisAccessFacts.AsPlatformScope(_tenantContext)
            .Where(f => f.OrganizationId == organizationId && f.AccessedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        // Rule evaluations and closed alerts follow the same retention (D3); open alerts stay until a person closes them.
        var evaluations = await _context.RuleEvaluations.AsPlatformScope(_tenantContext)
            .Where(e => e.OrganizationId == organizationId && e.StartedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var alerts = await _context.Alerts.AsPlatformScope(_tenantContext)
            .Where(a => a.OrganizationId == organizationId && a.LastSeenAt < cutoff && (a.Status == AlertStatus.Resolved || a.Status == AlertStatus.Dismissed))
            .ExecuteDeleteAsync(cancellationToken);

        // Periodic report deliveries: the files first, then the log rows.
        var expiredFiles = await _context.ReportDeliveries.AsPlatformScope(_tenantContext).AsNoTracking()
            .Where(d => d.OrganizationId == organizationId && d.CreatedAt < cutoff && d.StorageKey != null).Select(d => d.StorageKey!).ToListAsync(cancellationToken);
        foreach (var key in expiredFiles)
        {
            await _storage.DeleteAsync(key, cancellationToken);
        }

        var deliveries = await _context.ReportDeliveries.AsPlatformScope(_tenantContext)
            .Where(d => d.OrganizationId == organizationId && d.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        await _usage.AppendAndSaveAsync(UsageEventTypes.RetentionPurged, "Organization", organizationId.ToString(), quantity: deleted,
            metadata: new { months, cutoff, evaluations, alerts, deliveries }, organizationId: organizationId, cancellationToken: cancellationToken);
    }

    public static int EffectiveRetentionMonths(Organization organization, MonitoringOptions options)
    {
        ArgumentNullException.ThrowIfNull(organization);
        ArgumentNullException.ThrowIfNull(options);
        var max = Math.Max(1, options.MaxRetentionMonths);
        var configured = organization.MonitoringRetentionMonths <= 0 ? options.DefaultRetentionMonths : organization.MonitoringRetentionMonths;
        return Math.Clamp(configured, 1, max);
    }
}

/// <summary>Operations health: reports jobs that are overdue by more than fifteen minutes and not leased (the scheduler is not running, or keeps failing).</summary>
public sealed class SchedulerHealthCheck : IHealthCheck
{
    private readonly ConfigurationDbContext _context;
    private readonly IClock _clock;
    private readonly MonitoringOptions _options;

    public SchedulerHealthCheck(ConfigurationDbContext context, IClock clock, IOptions<MonitoringOptions> options)
    {
        _context = context;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var threshold = now.AddMinutes(-15);
        var overdue = await _context.ScheduledJobs.AsNoTracking().CountAsync(j => j.IsEnabled && j.NextRunAt < threshold && (j.LeaseUntil == null || j.LeaseUntil < now), cancellationToken);
        var failing = await _context.ScheduledJobs.AsNoTracking().CountAsync(j => j.IsEnabled && j.ConsecutiveFailures >= 3, cancellationToken);
        var total = await _context.ScheduledJobs.AsNoTracking().CountAsync(j => j.IsEnabled, cancellationToken);
        var data = new Dictionary<string, object> { ["jobs"] = total, ["overdue"] = overdue, ["failing"] = failing, ["schedulerEnabled"] = _options.SchedulerEnabled };
        if (!_options.SchedulerEnabled)
        {
            return HealthCheckResult.Degraded("The scheduler is disabled in this process.", data: data);
        }

        return overdue > 0 || failing > 0
            ? HealthCheckResult.Degraded($"{overdue} overdue job(s), {failing} failing job(s).", data: data)
            : HealthCheckResult.Healthy($"{total} job(s) on schedule.", data);
    }
}
