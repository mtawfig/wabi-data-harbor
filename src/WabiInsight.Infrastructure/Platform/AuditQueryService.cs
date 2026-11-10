using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Support;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Platform;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Platform;

/// <summary>
/// Read-only access to the audit trail for platform administrators. Details never contain secrets or business data.
/// A row that belongs to an organization is shown with reduced details (P13.26) unless the reader holds an approved,
/// unexpired support grant for that organization or performed the action themselves; the search follows the same
/// rule, so withheld text cannot be found by guessing it.
/// </summary>
public sealed class AuditQueryService : IAuditQueryService
{
    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public AuditQueryService(ConfigurationDbContext context, ITenantContext tenantContext, IClock clock)
    {
        _context = context;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<PagedResult<AuditLogRow>> ListAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        Require();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var logs = _context.AuditLogs.AsNoTracking();
        var readerId = _tenantContext.Principal.AdministratorId!.Value;
        var granted = await GrantedOrganizationsAsync(readerId, cancellationToken);

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
            // Details are searched only where the reader may read them in full.
            logs = logs.Where(l => l.ActorDisplayName.Contains(term)
                || (l.EntityId != null && l.EntityId.Contains(term))
                || (l.DetailsJson != null && l.DetailsJson.Contains(term)
                    && (l.OrganizationId == null || granted.Contains(l.OrganizationId.Value) || (l.ActorType == PrincipalType.PlatformAdministrator && l.ActorId == readerId))));
        }

        var total = await logs.LongCountAsync(cancellationToken);
        var items = await logs
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new AuditLogRow(l.AuditLogId, l.CreatedAt, l.OrganizationId, l.ActorType, l.ActorId, l.ActorDisplayName, l.EventType, l.EntityType, l.EntityId, l.DetailsJson, l.CorrelationId, l.IpAddress))
            .ToListAsync(cancellationToken);

        var shown = items.Select(row => MayReadInFull(row, readerId, granted) ? row : Reduce(row)).ToList();
        return new PagedResult<AuditLogRow>(shown, page, pageSize, (int)Math.Min(total, int.MaxValue));
    }

    private static bool MayReadInFull(AuditLogRow row, Guid readerId, HashSet<Guid> granted)
        => row.OrganizationId is not { } organizationId
           || granted.Contains(organizationId)
           || (row.ActorType == PrincipalType.PlatformAdministrator && row.ActorId == readerId);

    private static AuditLogRow Reduce(AuditLogRow row)
    {
        var (details, reduced) = AuditDetailReducer.Reduce(row.DetailsJson);
        var entityId = AuditDetailReducer.ReduceEntityId(row.EntityId);
        return row with { DetailsJson = details, EntityId = entityId, DetailsReduced = reduced || entityId != row.EntityId };
    }

    /// <summary>Organizations for which the reader holds an approved, unexpired support grant.</summary>
    private async Task<HashSet<Guid>> GrantedOrganizationsAsync(Guid administratorId, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var ids = await _context.SupportAccessGrants.AsPlatformScope(_tenantContext).AsNoTracking()
            .Where(g => g.AssignedPlatformAdministratorId == administratorId
                && (g.Status == SupportAccessGrantStatus.Approved || g.Status == SupportAccessGrantStatus.Active)
                && g.ExpiresAt != null && g.ExpiresAt > now)
            .Select(g => g.OrganizationId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return [.. ids];
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
