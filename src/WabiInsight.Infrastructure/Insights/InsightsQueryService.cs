using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Entities.Organizations;
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
/// no request can widen or move the scope. In P7.1 Organization Administrators have every approved metric;
/// per-role metric grants arrive in P7.2.
/// </summary>
public sealed class InsightsQueryService : IInsightsQueryService
{
    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly InsightsConnectionFactory _connections;
    private readonly IUsageEventWriter _usage;

    public InsightsQueryService(ConfigurationDbContext context, ITenantContext tenantContext, IInsightsConnectionFactory connections, IUsageEventWriter usage)
    {
        _context = context;
        _tenantContext = tenantContext;
        _connections = (InsightsConnectionFactory)connections;
        _usage = usage;
    }

    public async Task<OrganizationInsightsCatalog> GetApprovedCatalogAsync(CancellationToken cancellationToken = default)
    {
        RequireOrganizationAdministrator();
        var objects = await _context.InsightsObjects.AsNoTracking().Where(o => o.IsApproved && o.IsPresent).OrderBy(o => o.SortOrder).ThenBy(o => o.ViewName).ToListAsync(cancellationToken);
        var ids = objects.Select(o => o.InsightsObjectId).ToList();
        var columns = await _context.InsightsColumns.AsNoTracking().Where(c => ids.Contains(c.InsightsObjectId) && c.IsApproved && c.IsPresent).OrderBy(c => c.OrdinalPosition).ToListAsync(cancellationToken);
        var metrics = await _context.InsightsMetrics.AsNoTracking().Where(m => m.IsApproved && ids.Contains(m.InsightsObjectId)).OrderBy(m => m.SortOrder).ThenBy(m => m.MetricCode).ToListAsync(cancellationToken);
        var names = objects.ToDictionary(o => o.InsightsObjectId, o => o.ViewName);
        return new OrganizationInsightsCatalog(
            objects.Select(o => new InsightsObjectDto(o.InsightsObjectId, o.ViewName, o.DisplayNameEn, o.DisplayNameAr, o.Description, o.IsApproved, o.IsPresent, o.SortOrder, o.UpdatedAt, columns.Where(c => c.InsightsObjectId == o.InsightsObjectId).Select(InsightsCatalogService.ToDto).ToList())).ToList(),
            metrics.Select(m => new InsightsMetricDto(m.InsightsMetricId, m.MetricCode, m.InsightsObjectId, names[m.InsightsObjectId], m.DisplayNameEn, m.DisplayNameAr, m.Description, m.Aggregate, m.ColumnName, m.DateColumn, InsightsCatalogService.ParseFilters(m.FiltersJson), m.Unit, m.IsApproved, m.SortOrder, m.UpdatedAt)).ToList());
    }

    public async Task<MetricResult> QueryMetricAsync(string metricCode, MetricQueryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organizationId = RequireOrganizationAdministrator();
        var metric = await _context.InsightsMetrics.AsNoTracking().SingleOrDefaultAsync(m => m.MetricCode == metricCode && m.IsApproved, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "insights_metric_not_found");
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
        catch (SqlException)
        {
            await _usage.AppendAndSaveAsync(UsageEventTypes.InsightsQueried, "InsightsMetric", metric.MetricCode, succeeded: false, metadata: new { metric.MetricCode, request.Bucket, request.GroupBy }, cancellationToken: cancellationToken);
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "insights_query_failed");
        }

        await _usage.AppendAndSaveAsync(UsageEventTypes.InsightsQueried, "InsightsMetric", metric.MetricCode, quantity: rows.Count, metadata: new { metric.MetricCode, request.Bucket, request.GroupBy, durationMs = watch.Elapsed.TotalMilliseconds }, cancellationToken: cancellationToken);
        return new MetricResult(metric.MetricCode, metric.Unit, rows, watch.Elapsed.TotalMilliseconds);
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
}
