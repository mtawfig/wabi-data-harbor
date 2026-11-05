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

    private async Task DenyAsync(SupportAccessGrant? grant, string operation, string? targetReference, string code, CancellationToken cancellationToken)
    {
        await _audit.AppendAndSaveAsync(AuditEvents.SupportOperationDenied, "SupportAccessGrant", grant?.GrantId.ToString(), new { operation, targetReference, code }, grant?.OrganizationId, cancellationToken);
        throw new SupportAccessDeniedException(code, true);
    }
}
