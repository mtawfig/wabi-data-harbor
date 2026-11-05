using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Insights;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Insights;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Insights;

/// <summary>Per-role Platform Insights metric grants (specification §12.2). Tenant filter plus explicit organization checks.</summary>
public sealed class MetricGrantService : IMetricGrantService
{
    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;

    public MetricGrantService(ConfigurationDbContext context, ITenantContext tenantContext, IAuditWriter audit, IClock clock)
    {
        _context = context;
        _tenantContext = tenantContext;
        _audit = audit;
        _clock = clock;
    }

    public async Task<RoleMetricGrantsDto> GetAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireAdmin();
        var role = await _context.Roles.AsNoTracking().SingleOrDefaultAsync(r => r.OrganizationId == organizationId && r.RoleId == roleId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "role_not_found");
        var metrics = await ApprovedMetricsAsync(_context, cancellationToken);
        var automatic = role.IsSystem && role.RoleCode == SystemRoles.OrganizationAdmin;
        var granted = automatic
            ? metrics.Select(m => m.MetricId).ToList()
            : await _context.RoleMetricGrants.AsNoTracking().Where(g => g.RoleId == roleId).Select(g => g.InsightsMetricId).ToListAsync(cancellationToken);
        return new RoleMetricGrantsDto(role.RoleId, role.RoleCode, automatic, metrics, granted);
    }

    public async Task<RoleMetricGrantsDto> SetAsync(Guid roleId, IReadOnlyCollection<Guid> metricIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metricIds);
        var organizationId = RequireAdmin();
        var role = await _context.Roles.AsNoTracking().SingleOrDefaultAsync(r => r.OrganizationId == organizationId && r.RoleId == roleId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "role_not_found");
        if (role.IsSystem && role.RoleCode == SystemRoles.OrganizationAdmin)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "organization_admin_metrics_automatic");
        }

        var requested = metricIds.Distinct().ToList();
        var approved = await _context.InsightsMetrics.AsNoTracking().Where(m => m.IsApproved && requested.Contains(m.InsightsMetricId)).Select(m => m.InsightsMetricId).ToListAsync(cancellationToken);
        if (approved.Count != requested.Count)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "insights_metric_not_found");
        }

        var existing = await _context.RoleMetricGrants.Where(g => g.RoleId == roleId).ToListAsync(cancellationToken);
        _context.RoleMetricGrants.RemoveRange(existing.Where(g => !approved.Contains(g.InsightsMetricId)));
        foreach (var metricId in approved.Where(id => existing.All(g => g.InsightsMetricId != id)))
        {
            _context.RoleMetricGrants.Add(new RoleMetricGrant { OrganizationId = organizationId, RoleId = roleId, InsightsMetricId = metricId, GrantedAt = _clock.UtcNow, GrantedByUserId = _tenantContext.Principal.UserId });
        }

        _audit.Append(AuditEvents.InsightsMetricGrantChanged, "Role", roleId.ToString(), new { role.RoleCode, before = existing.Count, after = approved.Count }, organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return await GetAsync(roleId, cancellationToken);
    }

    internal static async Task<List<InsightsMetricDto>> ApprovedMetricsAsync(ConfigurationDbContext context, CancellationToken cancellationToken)
    {
        var views = await context.InsightsObjects.AsNoTracking().Where(o => o.IsApproved && o.IsPresent).ToDictionaryAsync(o => o.InsightsObjectId, o => o.ViewName, cancellationToken);
        var metrics = await context.InsightsMetrics.AsNoTracking().Where(m => m.IsApproved).OrderBy(m => m.SortOrder).ThenBy(m => m.MetricCode).ToListAsync(cancellationToken);
        return metrics.Where(m => views.ContainsKey(m.InsightsObjectId))
            .Select(m => new InsightsMetricDto(m.InsightsMetricId, m.MetricCode, m.InsightsObjectId, views[m.InsightsObjectId], m.DisplayNameEn, m.DisplayNameAr, m.Description, m.Aggregate, m.ColumnName, m.DateColumn, InsightsCatalogService.ParseFilters(m.FiltersJson), m.Unit, m.IsApproved, m.SortOrder, m.UpdatedAt))
            .ToList();
    }

    private Guid RequireAdmin()
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        if (!_tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin))
        {
            throw new TenantContextException("Only organization administrators manage metric grants.");
        }

        return organizationId;
    }
}
