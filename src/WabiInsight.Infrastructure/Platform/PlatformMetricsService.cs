using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Platform;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Platform;

/// <summary>
/// Aggregated platform metrics for the operations dashboard (specification §13). Every query is an explicit
/// platform-scope aggregate over identity tables and the usage-event stream; application names, connection
/// details, prompts and business data are never read.
/// </summary>
public sealed class PlatformMetricsService : IPlatformMetricsService
{
    private static readonly string[] NotYetCollected =
    [
        "aiRequests", "analysesCompleted", "analysesFailed", "reportsGenerated", "emailDraftsCreated",
        "openTickets", "criticalIncidents", "avgFirstResponseMinutes", "avgResolutionMinutes",
    ];

    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;

    public PlatformMetricsService(ConfigurationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<PlatformDashboard> GetDashboardAsync(MetricPeriod period, MetricBucket bucket, Guid? organizationId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(period);
        if (!_tenantContext.IsPlatformAdministrator)
        {
            throw new TenantContextException("Platform metrics require a platform administrator principal.");
        }

        var from = period.From;
        var to = period.To;

        var organizations = _context.Organizations.AsNoTracking();
        var users = _context.OrganizationUsers.AsNoTracking().IgnoreQueryFilters();
        var applications = _context.Applications.AsNoTracking().IgnoreQueryFilters();
        var events = _context.UsageEvents.AsNoTracking().Where(e => e.CreatedAt >= from && e.CreatedAt <= to);
        if (organizationId is { } scopedOrganization)
        {
            organizations = organizations.Where(o => o.OrganizationId == scopedOrganization);
            users = users.Where(u => u.OrganizationId == scopedOrganization);
            applications = applications.Where(a => a.OrganizationId == scopedOrganization);
            events = events.Where(e => e.OrganizationId == scopedOrganization);
        }

        // Daily event aggregates: one row per (day, organization, type, success) keeps the transfer small.
        var daily = await events
            .GroupBy(e => new { Day = e.CreatedAt.Date, e.OrganizationId, e.EventType, e.Succeeded })
            .Select(g => new { g.Key.Day, g.Key.OrganizationId, g.Key.EventType, g.Key.Succeeded, Count = g.LongCount() })
            .ToListAsync(cancellationToken);

        var dailyActors = await events
            .Where(e => e.EventType == UsageEventTypes.Login && e.ActorType == PrincipalType.OrganizationUser && e.ActorId != null)
            .GroupBy(e => new { Day = e.CreatedAt.Date, e.OrganizationId, e.ActorId })
            .Select(g => new { g.Key.Day, g.Key.OrganizationId, ActorId = g.Key.ActorId!.Value })
            .ToListAsync(cancellationToken);

        var kpis = new List<MetricValue>
        {
            new("totalOrganizations", await organizations.LongCountAsync(cancellationToken)),
            new("activeOrganizations", await organizations.LongCountAsync(o => o.Status == OrganizationStatus.Active, cancellationToken)),
            new("suspendedOrganizations", await organizations.LongCountAsync(o => o.Status == OrganizationStatus.Suspended, cancellationToken)),
            new("totalOrganizationUsers", await users.LongCountAsync(cancellationToken)),
            new("activeUsersInPeriod", dailyActors.Select(a => a.ActorId).Distinct().LongCount()),
            new("configuredApplications", await applications.LongCountAsync(a => !a.IsBuiltIn, cancellationToken)),
            new("platformAdministrators", await _context.Administrators.AsNoTracking().LongCountAsync(a => a.IsActive, cancellationToken)),
            new("logins", Sum(daily, UsageEventTypes.Login, true)),
            new("failedLogins", Sum(daily, UsageEventTypes.LoginFailed, false)),
            new("emailsSent", Sum(daily, UsageEventTypes.EmailSent, true)),
            new("emailsFailed", Sum(daily, UsageEventTypes.EmailFailed, false)),
            new("organizationsCreatedInPeriod", Sum(daily, UsageEventTypes.OrganizationCreated, true)),
            new("usersCreatedInPeriod", Sum(daily, UsageEventTypes.UserCreated, true)),
            new("apiErrors", Sum(daily, UsageEventTypes.ApiError, false)),
        };
        kpis.AddRange(NotYetCollected.Select(code => new MetricValue(code, 0, Collected: false)));

        var buckets = BucketStarts(from, to, bucket);
        MetricSeries SeriesOf(string code, string eventType, bool succeeded) => new(code, buckets.Select(b => new MetricPoint(b, daily.Where(d => d.EventType == eventType && d.Succeeded == succeeded && BucketOf(d.Day, bucket) == b).Sum(d => d.Count))).ToList());

        var organizationsOverTime = await organizations.Where(o => o.CreatedAt >= from && o.CreatedAt <= to).Select(o => o.CreatedAt).ToListAsync(cancellationToken);
        var series = new List<MetricSeries>
        {
            new("organizationsRegistered", buckets.Select(b => new MetricPoint(b, organizationsOverTime.LongCount(c => BucketOf(c.Date, bucket) == b))).ToList()),
            new("activeUsers", buckets.Select(b => new MetricPoint(b, dailyActors.Where(a => BucketOf(a.Day, bucket) == b).Select(a => a.ActorId).Distinct().LongCount())).ToList()),
            SeriesOf("logins", UsageEventTypes.Login, true),
            SeriesOf("failedLogins", UsageEventTypes.LoginFailed, false),
            SeriesOf("emailsSent", UsageEventTypes.EmailSent, true),
            SeriesOf("emailsFailed", UsageEventTypes.EmailFailed, false),
            SeriesOf("usersCreated", UsageEventTypes.UserCreated, true),
            SeriesOf("apiErrors", UsageEventTypes.ApiError, false),
        };

        var organizationRows = await organizations
            .OrderBy(o => o.OrganizationName)
            .Select(o => new { o.OrganizationId, o.OrganizationCode, o.OrganizationName, o.Status, Users = _context.OrganizationUsers.IgnoreQueryFilters().LongCount(u => u.OrganizationId == o.OrganizationId) })
            .ToListAsync(cancellationToken);
        var usage = organizationRows.Select(o => new OrganizationUsageRow(
            o.OrganizationId,
            o.OrganizationCode,
            o.OrganizationName,
            o.Status.ToString(),
            o.Users,
            dailyActors.Where(a => a.OrganizationId == o.OrganizationId).Select(a => a.ActorId).Distinct().LongCount(),
            daily.Where(d => d.OrganizationId == o.OrganizationId && d.EventType == UsageEventTypes.Login && d.Succeeded).Sum(d => d.Count),
            daily.Where(d => d.OrganizationId == o.OrganizationId && d.EventType == UsageEventTypes.EmailSent && d.Succeeded).Sum(d => d.Count),
            daily.Where(d => d.OrganizationId == o.OrganizationId && d.EventType == UsageEventTypes.EmailFailed && !d.Succeeded).Sum(d => d.Count))).ToList();

        var errors = await events
            .Where(e => !e.Succeeded)
            .OrderByDescending(e => e.CreatedAt)
            .Take(50)
            .Select(e => new PlatformErrorRow(e.CreatedAt, e.EventType, e.OrganizationId, e.MetadataJson))
            .ToListAsync(cancellationToken);

        return new PlatformDashboard(period, kpis, series, usage, errors);
    }

    private static long Sum(IEnumerable<dynamic> daily, string eventType, bool succeeded)
    {
        long total = 0;
        foreach (var row in daily)
        {
            if (row.EventType == eventType && row.Succeeded == succeeded)
            {
                total += (long)row.Count;
            }
        }

        return total;
    }

    private static List<DateTimeOffset> BucketStarts(DateTimeOffset from, DateTimeOffset to, MetricBucket bucket)
    {
        var starts = new List<DateTimeOffset>();
        var cursor = BucketOf(from.Date, bucket);
        var end = BucketOf(to.Date, bucket);
        while (cursor <= end && starts.Count < 400)
        {
            starts.Add(cursor);
            cursor = bucket switch
            {
                MetricBucket.Day => cursor.AddDays(1),
                MetricBucket.Week => cursor.AddDays(7),
                _ => cursor.AddMonths(1),
            };
        }

        return starts;
    }

    private static DateTimeOffset BucketOf(DateTime day, MetricBucket bucket)
    {
        var date = new DateTimeOffset(day.Year, day.Month, day.Day, 0, 0, 0, TimeSpan.Zero);
        return bucket switch
        {
            MetricBucket.Day => date,
            MetricBucket.Week => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)), // Monday
            _ => new DateTimeOffset(day.Year, day.Month, 1, 0, 0, 0, TimeSpan.Zero),
        };
    }
}
