using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Platform;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Platform;

/// <summary>Read-only access to the audit trail for platform administrators. Details never contain secrets or business data.</summary>
public sealed class AuditQueryService : IAuditQueryService
{
    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;

    public AuditQueryService(ConfigurationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<PagedResult<AuditLogRow>> ListAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        Require();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var logs = _context.AuditLogs.AsNoTracking();

        if (query.From is { } from)
        {
            logs = logs.Where(l => l.CreatedAt >= from);
        }

        if (query.To is { } to)
        {
            logs = logs.Where(l => l.CreatedAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            var type = query.EventType.Trim();
            logs = logs.Where(l => l.EventType == type);
        }

        if (query.OrganizationId is { } organizationId)
        {
            logs = logs.Where(l => l.OrganizationId == organizationId);
        }

        if (query.ActorType is { } actorType)
        {
            logs = logs.Where(l => l.ActorType == actorType);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            logs = logs.Where(l => l.ActorDisplayName.Contains(term) || (l.EntityId != null && l.EntityId.Contains(term)) || (l.DetailsJson != null && l.DetailsJson.Contains(term)));
        }

        var total = await logs.LongCountAsync(cancellationToken);
        var items = await logs
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new AuditLogRow(l.AuditLogId, l.CreatedAt, l.OrganizationId, l.ActorType, l.ActorId, l.ActorDisplayName, l.EventType, l.EntityType, l.EntityId, l.DetailsJson, l.CorrelationId, l.IpAddress))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogRow>(items, page, pageSize, (int)Math.Min(total, int.MaxValue));
    }

    public async Task<IReadOnlyList<string>> ListEventTypesAsync(CancellationToken cancellationToken = default)
    {
        Require();
        return await _context.AuditLogs.AsNoTracking().Select(l => l.EventType).Distinct().OrderBy(t => t).ToListAsync(cancellationToken);
    }

    private void Require()
    {
        if (!_tenantContext.IsPlatformAdministrator)
        {
            throw new TenantContextException("Audit logs require a platform administrator principal.");
        }
    }
}
