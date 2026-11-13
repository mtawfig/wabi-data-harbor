using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Entities.Insights;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Platform;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Insights;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Tenancy;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Insights;

/// <summary>
/// Runs approved metrics for the signed-in organization (specification §12). The organization id is taken from
/// the principal and bound as <c>@tenant</c>; the builder refuses any reference to the organization column, so
/// no request can widen or move the scope. Organization Administrators have every approved metric; other users
/// only the metrics granted to their active roles (specification §12.2).
/// </summary>
public sealed partial class InsightsQueryService : IInsightsQueryService
{
    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly InsightsConnectionFactory _connections;
    private readonly IUsageEventWriter _usage;
    private readonly ILogger<InsightsQueryService> _logger;

    public InsightsQueryService(ConfigurationDbContext context, ITenantContext tenantContext, IInsightsConnectionFactory connections, IUsageEventWriter usage, ILogger<InsightsQueryService>? logger = null)
    {
        _logger = logger ?? NullLogger<InsightsQueryService>.Instance;
        _context = context;
        _tenantContext = tenantContext;
        _connections = (InsightsConnectionFactory)connections;
        _usage = usage;
    }

    public async Task<OrganizationInsightsCatalog> GetApprovedCatalogAsync(CancellationToken cancellationToken = default)
    {
        _tenantContext.RequireOrganizationId();
        var metrics = await AllowedMetricsAsync(cancellationToken);
        var ids = metrics.Select(m => m.ObjectId).Distinct().ToList();
        var objects = await _context.InsightsObjects.AsNoTracking().Where(o => ids.Contains(o.InsightsObjectId)).OrderBy(o => o.SortOrder).ThenBy(o => o.ViewName).ToListAsync(cancellationToken);
        var columns = await _context.InsightsColumns.AsNoTracking().Where(c => ids.Contains(c.InsightsObjectId) && c.IsApproved && c.IsPresent).OrderBy(c => c.OrdinalPosition).ToListAsync(cancellationToken);
        return new OrganizationInsightsCatalog(
            objects.Select(o => new InsightsObjectDto(o.InsightsObjectId, o.ViewName, o.DisplayNameEn, o.DisplayNameAr, o.Description, o.IsApproved, o.IsPresent, o.SortOrder, o.UpdatedAt, columns.Where(c => c.InsightsObjectId == o.InsightsObjectId).Select(InsightsCatalogService.ToDto).ToList())).ToList(),
            metrics);
    }

    /// <summary>Approved metrics for Organization Administrators; otherwise the ones granted to the user's active roles.</summary>
    private async Task<List<InsightsMetricDto>> AllowedMetricsAsync(CancellationToken cancellationToken)
    {
        var approved = await MetricGrantService.ApprovedMetricsAsync(_context, cancellationToken);
        if (_tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin))
        {
            return approved;
        }

        var userId = _tenantContext.Principal.UserId!.Value;
        var roleIds = await _context.UserRoles.AsNoTracking().Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync(cancellationToken);
        var activeRoleIds = await _context.Roles.AsNoTracking().Where(r => roleIds.Contains(r.RoleId) && r.IsActive).Select(r => r.RoleId).ToListAsync(cancellationToken);
        var granted = await _context.RoleMetricGrants.AsNoTracking().Where(g => activeRoleIds.Contains(g.RoleId)).Select(g => g.InsightsMetricId).ToListAsync(cancellationToken);
        var grantedSet = granted.ToHashSet();
        return approved.Where(m => grantedSet.Contains(m.MetricId)).ToList();
    }

    public async Task<MetricResult> QueryMetricAsync(string metricCode, MetricQueryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organizationId = _tenantContext.RequireOrganizationId();
        var allowed = await AllowedMetricsAsync(cancellationToken);
        if (!allowed.Any(m => string.Equals(m.MetricCode, metricCode, StringComparison.Ordinal)))
        {
            throw new DomainException(OrganizationOperationOutcome.NotFound, "insights_metric_not_found");
        }

        var metric = await _context.InsightsMetrics.AsNoTracking().SingleAsync(m => m.MetricCode == metricCode && m.IsApproved, cancellationToken);
        return await RunAsync(metric, request, organizationId, cancellationToken);
    }

    private async Task<MetricResult> RunAsync(InsightsMetric metric, MetricQueryRequest request, Guid organizationId, CancellationToken cancellationToken)
    {
        var shape = await InsightsCatalogService.ShapeAsync(_context, metric.InsightsObjectId, requireApproved: true, cancellationToken);
        var query = InsightsSqlBuilder.Build(InsightsCatalogService.ToDefinition(metric), shape, request, organizationId);

        var watch = Stopwatch.StartNew();
        var rows = new List<MetricRow>();
        try
        {
            await using var connection = (SqlConnection)await _connections.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = query.Sql;
            command.CommandTimeout = _connections.CommandTimeoutSeconds;
            foreach (var parameter in query.Parameters)
            {
                command.Parameters.AddWithValue(parameter.Key, parameter.Value);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var bucketOrdinal = Ordinal(reader, "Bucket");
            var groupOrdinal = Ordinal(reader, "Group");
            var valueOrdinal = reader.GetOrdinal("Value");
            while (await reader.ReadAsync(cancellationToken))
            {
                DateTimeOffset? bucket = bucketOrdinal is { } b && !reader.IsDBNull(b) ? new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(b), DateTimeKind.Utc)) : null;
                string? group = groupOrdinal is { } g && !reader.IsDBNull(g) ? Convert.ToString(reader.GetValue(g), CultureInfo.InvariantCulture) : null;
                var value = reader.IsDBNull(valueOrdinal) ? 0d : Convert.ToDouble(reader.GetValue(valueOrdinal), CultureInfo.InvariantCulture);
                rows.Add(new MetricRow(bucket, group, value));
            }
        }
        catch (SqlException ex)
        {
            // The person sees a short message; the reason goes to the log, where an administrator can act on it.
            LogQueryFailed(_logger, metric.MetricCode, ex.Number, ex.Message);
            await _usage.AppendAndSaveAsync(UsageEventTypes.InsightsQueried, "InsightsMetric", metric.MetricCode, succeeded: false, metadata: new { metric.MetricCode, request.Bucket, request.GroupBy }, cancellationToken: cancellationToken);
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "insights_query_failed");
        }

        await _usage.AppendAndSaveAsync(UsageEventTypes.InsightsQueried, "InsightsMetric", metric.MetricCode, quantity: rows.Count, metadata: new { metric.MetricCode, request.Bucket, request.GroupBy, durationMs = watch.Elapsed.TotalMilliseconds }, cancellationToken: cancellationToken);
        return new MetricResult(metric.MetricCode, metric.Unit, rows, watch.Elapsed.TotalMilliseconds);
    }

    public async Task<OrganizationDashboard> GetDashboardAsync(MetricPeriod period, MetricBucket bucket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(period);
        var organizationId = RequireOrganizationAdministrator();
        var metrics = await _context.InsightsMetrics.AsNoTracking().Where(m => m.IsApproved).ToDictionaryAsync(m => m.MetricCode, cancellationToken);
        var kpis = new List<MetricValue>();
        var series = new List<MetricSeries>();

        async Task<double?> ValueAsync(string code, bool periodic)
        {
            if (!metrics.TryGetValue(code, out var metric))
            {
                return null;
            }

            var request = periodic ? new MetricQueryRequest(period.From, period.To, null, null, null) : new MetricQueryRequest(null, null, null, null, null);
            var result = await RunAsync(metric, request, organizationId, cancellationToken);
            return result.Rows.Count == 0 ? 0 : result.Rows[0].Value;
        }

        async Task KpiAsync(string kpiCode, string metricCode, bool periodic)
        {
            var value = await ValueAsync(metricCode, periodic);
            kpis.Add(value is null ? new MetricValue(kpiCode, 0, Collected: false) : new MetricValue(kpiCode, (long)Math.Round(value.Value)));
        }

        async Task SeriesAsync(string seriesCode, string metricCode)
        {
            if (!metrics.TryGetValue(metricCode, out var metric) || metric.DateColumn is null)
            {
                return;
            }

            var result = await RunAsync(metric, new MetricQueryRequest(period.From, period.To, bucket, null, null), organizationId, cancellationToken);
            var values = result.Rows.Where(r => r.Bucket is not null).ToDictionary(r => r.Bucket!.Value.UtcDateTime.Date, r => (long)Math.Round(r.Value));
            series.Add(new MetricSeries(seriesCode, Buckets(period, bucket).Select(b => new MetricPoint(new DateTimeOffset(b, TimeSpan.Zero), values.GetValueOrDefault(b))).ToList()));
        }

        await KpiAsync("totalUsers", "users.total", false);
        await KpiAsync("activeUsers", "users.active", false);
        await KpiAsync("usersSignedIn", "users.loggedIn", true);
        await KpiAsync("departments", "departments.active", false);
        await KpiAsync("configuredApplications", "applications.configured", false);
        await KpiAsync("approvedObjects", "applications.approvedObjects", false);
        await KpiAsync("logins", "logins.total", true);
        await KpiAsync("failedLogins", "logins.failed", true);
        await KpiAsync("failedOperations", "usage.failedOperations", true);
        await KpiAsync("ticketsCreated", "tickets.total", true);
        await KpiAsync("openTickets", "tickets.open", false);
        await KpiAsync("avgFirstResponseMinutes", "tickets.avgFirstResponseMinutes", true);
        await KpiAsync("avgResolutionMinutes", "tickets.avgResolutionMinutes", true);
        await KpiAsync("supportGrants", "supportAccess.grants", true);
        await KpiAsync("emailsSent", "emails.sent", true);
        await KpiAsync("emailsFailed", "emails.failed", true);
        foreach (var later in new[] { "analysesCompleted", "analysesFailed", "reportsGenerated", "aiTokens", "internalShares", "externalShares" })
        {
            kpis.Add(new MetricValue(later, 0, Collected: false));
        }

        await SeriesAsync("logins", "logins.total");
        await SeriesAsync("failedLogins", "logins.failed");
        await SeriesAsync("activeUsers", "users.loggedIn");
        await SeriesAsync("ticketsCreated", "tickets.total");
        await SeriesAsync("emailsSent", "emails.sent");
        await SeriesAsync("emailsFailed", "emails.failed");
        await SeriesAsync("failedOperations", "usage.failedOperations");

        var departments = await _context.Departments.AsNoTracking().ToDictionaryAsync(d => d.DepartmentId, cancellationToken);
        var departmentUsage = new List<DashboardTableRow>();
        if (metrics.TryGetValue("usage.events", out var usage))
        {
            var rows = await RunAsync(usage, new MetricQueryRequest(period.From, period.To, null, "DepartmentId", null), organizationId, cancellationToken);
            departmentUsage = rows.Rows.Select(r =>
            {
                var department = r.Group is not null && Guid.TryParse(r.Group, out var id) && departments.TryGetValue(id, out var d) ? d : null;
                return new DashboardTableRow(r.Group ?? "none", department?.DepartmentNameEn ?? string.Empty, department?.DepartmentNameAr, r.Value, null);
            }).OrderByDescending(r => r.Value).ToList();
        }

        var ticketsByStatus = new List<DashboardTableRow>();
        if (metrics.TryGetValue("tickets.total", out var tickets))
        {
            var rows = await RunAsync(tickets, new MetricQueryRequest(period.From, period.To, null, "Status", null), organizationId, cancellationToken);
            ticketsByStatus = rows.Rows.Select(r => new DashboardTableRow(r.Group ?? "unknown", r.Group ?? string.Empty, null, r.Value, null)).ToList();
        }

        var applications = await _context.Applications.AsNoTracking().Where(a => a.IsActive).OrderByDescending(a => a.IsBuiltIn).ThenBy(a => a.ApplicationCode).ToListAsync(cancellationToken);
        var applicationIds = applications.Select(a => a.ApplicationId).ToList();
        var objectCounts = await _context.DatabaseObjects.AsNoTracking().Where(o => applicationIds.Contains(o.ApplicationId) && o.IsPresent)
            .GroupBy(o => o.ApplicationId).Select(g => new { ApplicationId = g.Key, Total = g.LongCount(), Approved = g.LongCount(o => o.IsApproved) }).ToDictionaryAsync(x => x.ApplicationId, cancellationToken);
        var applicationRows = applications.Select(a =>
        {
            var counts = objectCounts.GetValueOrDefault(a.ApplicationId);
            return new DashboardApplicationRow(a.ApplicationId, a.ApplicationCode, a.ApplicationName, a.ApplicationNameAr, a.DatabaseType.ToString(), a.IsBuiltIn, a.IsApproved, a.IsActive, counts?.Total ?? 0, counts?.Approved ?? 0, a.LastMetadataRefreshAt);
        }).ToList();

        return new OrganizationDashboard(period, kpis, series, departmentUsage, ticketsByStatus, applicationRows);
    }

    /// <summary>Every bucket start inside the period, so charts show zeros instead of gaps. Week buckets start on Monday, like the SQL expression.</summary>
    internal static IEnumerable<DateTime> Buckets(MetricPeriod period, MetricBucket bucket)
    {
        var start = period.From.UtcDateTime.Date;
        var end = period.To.UtcDateTime.Date;
        start = bucket switch
        {
            MetricBucket.Week => start.AddDays(-(((int)start.DayOfWeek + 6) % 7)),
            MetricBucket.Month => new DateTime(start.Year, start.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => start,
        };
        for (var current = start; current <= end; current = bucket switch { MetricBucket.Week => current.AddDays(7), MetricBucket.Month => current.AddMonths(1), _ => current.AddDays(1) })
        {
            yield return DateTime.SpecifyKind(current, DateTimeKind.Utc);
        }
    }

    private static int? Ordinal(SqlDataReader reader, string name)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return null;
    }

    private Guid RequireOrganizationAdministrator()
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        if (!_tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin))
        {
            throw new TenantContextException("Platform Insights metrics are available to organization administrators in this phase.");
        }

        return organizationId;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Insights metric {MetricCode} could not be queried (SQL error {Number}: {Reason}).")]
    private static partial void LogQueryFailed(ILogger logger, string metricCode, int number, string reason);
}
