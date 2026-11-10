using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Support;
using WabiInsight.Core.SupportAccess;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.SupportAccess;

/// <summary>
/// The dedicated authorization handler for support mode (specification §16.3). Every operation is checked
/// against the caller's active session: assigned administrator, grant usable and unexpired, operation and
/// target inside the approved scope. Denials are audited.
/// </summary>
public sealed class SupportAccessAuthorizer : ISupportAccessAuthorizer
{
    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly ISupportAccessService _grants;

    public SupportAccessAuthorizer(ConfigurationDbContext context, ITenantContext tenantContext, IClock clock, IAuditWriter audit, ISupportAccessService grants)
    {
        _context = context;
        _tenantContext = tenantContext;
        _clock = clock;
        _audit = audit;
        _grants = grants;
    }

    public async Task<bool> HasActiveSessionAsync(CancellationToken cancellationToken = default)
        => _tenantContext.IsPlatformAdministrator && await _grants.CurrentSessionAsync(cancellationToken) is not null;

    public async Task<SupportExecutionContext> AuthorizeAsync(string operation, string? targetReference, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsPlatformAdministrator)
        {
            throw new SupportAccessDeniedException("support_session_required", true);
        }

        var administratorId = _tenantContext.Principal.AdministratorId!.Value;
        var session = await _context.SupportSessions.AsPlatformScope(_tenantContext).SingleOrDefaultAsync(s => s.AdministratorId == administratorId && s.EndedAt == null, cancellationToken);
        if (session is null)
        {
            await DenyAsync(null, operation, targetReference, "support_session_required", cancellationToken);
        }

        var grant = await _context.SupportAccessGrants.AsPlatformScope(_tenantContext).AsNoTracking().SingleAsync(g => g.GrantId == session!.GrantId, cancellationToken);
        var now = _clock.UtcNow;
        if (grant.AssignedPlatformAdministratorId != administratorId)
        {
            await DenyAsync(grant, operation, targetReference, "support_grant_not_assigned_to_you", cancellationToken);
        }

        if (SupportAccessScope.HasExpired(grant.Status, grant.ExpiresAt, now))
        {
            await _grants.ExpireAsync(cancellationToken);
            await DenyAsync(grant, operation, targetReference, "support_grant_expired", cancellationToken);
        }

        if (!SupportAccessScope.IsUsable(grant.Status, grant.ExpiresAt, now) || grant.Status != SupportAccessGrantStatus.Active)
        {
            await DenyAsync(grant, operation, targetReference, "support_grant_not_usable", cancellationToken);
        }

        var allowed = JsonSerializer.Deserialize<List<SupportOperationEntry>>(grant.AllowedOperationsJson, SupportAccessService.JsonOptions) ?? [];
        var error = SupportAccessScope.Validate(allowed, operation, targetReference);
        if (error is not null)
        {
            await DenyAsync(grant, operation, targetReference, error, cancellationToken);
        }

        return new SupportExecutionContext(grant.GrantId, session!.SessionId, grant.TicketId, grant.OrganizationId, administratorId, operation, targetReference?.Trim());
    }

    public async Task<IReadOnlyList<string>> AuthorizeRoleAssignmentAsync(SupportExecutionContext context, IReadOnlyCollection<string> roleCodes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(roleCodes);
        var requested = roleCodes.Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (requested.Count == 0)
        {
            return [];
        }

        // The approved list is read from the grant itself, never from the request: role ids the organization approved for this operation.
        var grant = await _context.SupportAccessGrants.AsPlatformScope(_tenantContext).AsNoTracking().SingleAsync(g => g.GrantId == context.GrantId && g.OrganizationId == context.OrganizationId, cancellationToken);
        var allowed = JsonSerializer.Deserialize<List<SupportOperationEntry>>(grant.AllowedOperationsJson, SupportAccessService.JsonOptions) ?? [];
        var definition = SupportOperations.Find(context.Operation);
        var entry = allowed.FirstOrDefault(a => string.Equals(a.Operation, context.Operation, StringComparison.Ordinal));
        var grantedIds = definition?.TargetType == SupportTargetType.AssignableRole && entry is not null
            ? entry.TargetReferences.Select(t => Guid.TryParse(t, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).Distinct().ToList()
            : [];
        var grantedCodes = grantedIds.Count == 0
            ? []
            : await _context.Roles.AsPlatformScope(_tenantContext).AsNoTracking()
                .Where(r => r.OrganizationId == context.OrganizationId && grantedIds.Contains(r.RoleId))
                .Select(r => r.RoleCode)
                .ToListAsync(cancellationToken);
        var refused = SupportAccessScope.RolesOutsideGrant(grantedCodes, requested);
        if (refused.Count > 0)
        {
            await DenyAsync(grant, context.Operation, context.TargetReference, "support_role_not_granted", refused, cancellationToken);
        }

        // The codes as stored, so what is assigned is exactly what was checked.
        return grantedCodes.Where(code => requested.Contains(code, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    private Task DenyAsync(SupportAccessGrant? grant, string operation, string? targetReference, string code, CancellationToken cancellationToken)
        => DenyAsync(grant, operation, targetReference, code, null, cancellationToken);

    private async Task DenyAsync(SupportAccessGrant? grant, string operation, string? targetReference, string code, IReadOnlyList<string>? roleCodes, CancellationToken cancellationToken)
    {
        object details = roleCodes is null ? new { operation, targetReference, code } : new { operation, targetReference, code, roleCodes };
        await _audit.AppendAndSaveAsync(AuditEvents.SupportOperationDenied, "SupportAccessGrant", grant?.GrantId.ToString(), details, grant?.OrganizationId, cancellationToken);
        throw new SupportAccessDeniedException(code, true);
    }
}
