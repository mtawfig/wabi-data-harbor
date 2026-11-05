using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Email;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Entities.Support;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Notifications;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Security;
using WabiInsight.Core.SupportAccess;
using WabiInsight.Core.Tenancy;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.SupportAccess;

/// <summary>
/// Grant lifecycle (specification §16). Who may do what is resolved from the tenant context only; every
/// transition lands on the ticket timeline, in the audit trail and in the notification centre.
/// </summary>
public sealed class SupportAccessService : ISupportAccessService
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly IUsageEventWriter _usage;
    private readonly INotificationDispatcher _notifications;
    private readonly SupportAccessOptions _options;

    public SupportAccessService(ConfigurationDbContext context, ITenantContext tenantContext, IClock clock, IAuditWriter audit, IUsageEventWriter usage, INotificationDispatcher notifications, IOptions<SupportAccessOptions> options)
    {
        _context = context;
        _tenantContext = tenantContext;
        _clock = clock;
        _audit = audit;
        _usage = usage;
        _notifications = notifications;
        _options = options.Value;
    }

    public IReadOnlyList<SupportOperationDefinition> Catalog => SupportOperations.Catalog;

    public async Task<SupportAccessGrantDto> ProposeAsync(Guid ticketId, ProposeGrantCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequirePlatformAdministrator();
        var entries = command.Operations.Select(o => new SupportOperationEntry(o.Operation.Trim(), o.TargetReferences.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList())).ToList();
        var errors = SupportAccessScope.ValidateProposal(entries, command.ExpiryHours).ToList();
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length > 2000)
        {
            errors.Add("support_reason_required");
        }

        if (command.ExpiryHours is { } hours && hours > _options.MaxExpiryHours)
        {
            errors.Add("support_expiry_out_of_range");
        }

        if (errors.Count > 0)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, [.. errors.Distinct(StringComparer.Ordinal)]);
        }

        var ticket = await _context.SupportTickets.AsPlatformScope(_tenantContext).SingleOrDefaultAsync(t => t.TicketId == ticketId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "ticket_not_found");
        if (ticket.Status is TicketStatus.Resolved or TicketStatus.Cancelled)
        {
            throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, "ticket_closed");
        }

        await ValidateTargetsAsync(ticket.OrganizationId, entries, cancellationToken);
        if (await _context.SupportAccessGrants.AsPlatformScope(_tenantContext).AnyAsync(g => g.TicketId == ticketId && (g.Status == SupportAccessGrantStatus.Proposed || g.Status == SupportAccessGrantStatus.Approved || g.Status == SupportAccessGrantStatus.Active), cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, "support_grant_already_open");
        }

        var now = _clock.UtcNow;
        var administratorId = _tenantContext.Principal.AdministratorId!.Value;
        var grant = new SupportAccessGrant
        {
            GrantId = Ids.New(),
            TicketId = ticket.TicketId,
            OrganizationId = ticket.OrganizationId,
            RequestedByUserId = ticket.CreatedByUserId,
            ProposedByAdministratorId = administratorId,
            AssignedPlatformAdministratorId = ticket.AssignedAdministratorId ?? administratorId,
            ScopeType = SupportAccessScopeType.Operations,
            AllowedOperationsJson = JsonSerializer.Serialize(entries, JsonOptions),
            ApprovedObjectReferences = string.Join(",", entries.SelectMany(e => e.TargetReferences)),
            Reason = command.Reason.Trim(),
            Status = SupportAccessGrantStatus.Proposed,
            CreatedAt = now,
        };
        if (command.ExpiryHours is { } requested)
        {
            // Stored as the requested duration until approval sets the real expiry.
            grant.DecisionNote = null;
            grant.ExpiresAt = now.AddHours(requested);
        }

        _context.SupportAccessGrants.Add(grant);
        if (!ticket.RequiresSupportAccess)
        {
            ticket.RequiresSupportAccess = true;
            AddTicketEvent(ticket, TicketEventTypes.SupportAccessFlagChanged, new { requiresSupportAccess = true });
        }

        ticket.LastActivityAt = now;
        AddTicketEvent(ticket, TicketEventTypes.SupportAccessProposed, new { grant.GrantId, operations = entries, grant.Reason });
        _audit.Append(AuditEvents.SupportAccessProposed, "SupportAccessGrant", grant.GrantId.ToString(), new { ticket.TicketId, ticket.ReferenceNumber, operations = entries, grant.Reason }, ticket.OrganizationId);

        var admins = await OrganizationAdminsAsync(ticket.OrganizationId, cancellationToken);
        var data = await DataAsync(grant, ticket, cancellationToken);
        _notifications.Notify(admins, NotificationTypes.SupportAccessProposed, "SupportAccessGrant", grant.GrantId.ToString(), data, ticket.OrganizationId);
        await _context.SaveChangesAsync(cancellationToken);
        await _notifications.EmailAsync(admins, EmailTemplateCodes.SupportNotification,
            $"[{ticket.ReferenceNumber}] Support access approval needed",
            $"[{ticket.ReferenceNumber}] مطلوب اعتماد وصول الدعم",
            $"{_tenantContext.Principal.DisplayName} proposes {entries.Count} support operation(s) on ticket {ticket.ReferenceNumber}. An organization administrator must approve before anything changes.",
            $"يقترح {_tenantContext.Principal.DisplayName} {entries.Count} عملية دعم على التذكرة {ticket.ReferenceNumber}. يجب أن يعتمدها مدير المؤسسة قبل أي تغيير.",
            $"app/support/{ticket.TicketId}", ticket.OrganizationId, cancellationToken);

        return await GetAsync(grant.GrantId, cancellationToken);
    }

    public async Task<SupportAccessGrantDto> ApproveAsync(Guid grantId, GrantDecisionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var organizationId = RequireOrganizationAdmin();
        var grant = await LoadVisibleAsync(grantId, cancellationToken);
        if (grant.Status != SupportAccessGrantStatus.Proposed)
        {
            throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, "support_grant_not_pending");
        }

        var userId = _tenantContext.Principal.UserId!.Value;
        var organization = await _context.Organizations.AsNoTracking().SingleAsync(o => o.OrganizationId == organizationId, cancellationToken);
        if (grant.RequestedByUserId == userId && organization.RequireSecondAdminApprovalForSupportAccess)
        {
            throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, "support_second_admin_required");
        }

        var hours = command.ExpiryHours ?? (grant.ExpiresAt is { } proposed ? Math.Max(1, (int)Math.Ceiling((proposed - grant.CreatedAt).TotalHours)) : _options.DefaultExpiryHours);
        if (hours < 1 || hours > _options.MaxExpiryHours)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "support_expiry_out_of_range");
        }

        var now = _clock.UtcNow;
        grant.Status = SupportAccessGrantStatus.Approved;
        grant.ApprovedByOrganizationAdminUserId = userId;
        grant.ApprovedAt = now;
        grant.ExpiresAt = now.AddHours(hours);
        grant.DecisionNote = Clean(command.Note);
        var ticket = await TicketOfAsync(grant, cancellationToken);
        ticket.LastActivityAt = now;
        AddTicketEvent(ticket, TicketEventTypes.SupportAccessApproved, new { grant.GrantId, grant.ExpiresAt, selfApproved = grant.RequestedByUserId == userId }, grant.DecisionNote);
        _audit.Append(AuditEvents.SupportAccessApproved, "SupportAccessGrant", grant.GrantId.ToString(), new { ticket.TicketId, ticket.ReferenceNumber, grant.ExpiresAt, selfApproved = grant.RequestedByUserId == userId }, organizationId);
        _usage.Append(UsageEventTypes.SupportGrantApproved, "SupportAccessGrant", grant.GrantId.ToString(), organizationId: organizationId);

        var assignee = await AdministratorRecipientsAsync(grant.AssignedPlatformAdministratorId, cancellationToken);
        _notifications.Notify(assignee, NotificationTypes.SupportAccessApproved, "SupportAccessGrant", grant.GrantId.ToString(), await DataAsync(grant, ticket, cancellationToken), organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        await _notifications.EmailAsync(assignee, EmailTemplateCodes.SupportNotification,
            $"[{ticket.ReferenceNumber}] Support access approved",
            $"[{ticket.ReferenceNumber}] تم اعتماد وصول الدعم",
            $"{_tenantContext.Principal.DisplayName} approved support access on ticket {ticket.ReferenceNumber} until {grant.ExpiresAt:u}.",
            $"اعتمد {_tenantContext.Principal.DisplayName} وصول الدعم على التذكرة {ticket.ReferenceNumber} حتى {grant.ExpiresAt:u}.",
            $"platform/support/{ticket.TicketId}", organizationId, cancellationToken);
        return await GetAsync(grant.GrantId, cancellationToken);
    }

    public async Task<SupportAccessGrantDto> DeclineAsync(Guid grantId, GrantDecisionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var organizationId = RequireOrganizationAdmin();
        var grant = await LoadVisibleAsync(grantId, cancellationToken);
        if (grant.Status != SupportAccessGrantStatus.Proposed)
        {
            throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, "support_grant_not_pending");
        }

        var now = _clock.UtcNow;
        grant.Status = SupportAccessGrantStatus.Declined;
        grant.DecisionNote = Clean(command.Note);
        grant.RevokedAt = now;
        grant.RevokedByType = PrincipalType.OrganizationUser;
        grant.RevokedById = _tenantContext.Principal.UserId;
        var ticket = await TicketOfAsync(grant, cancellationToken);
        ticket.LastActivityAt = now;
        AddTicketEvent(ticket, TicketEventTypes.SupportAccessDeclined, new { grant.GrantId }, grant.DecisionNote);
        _audit.Append(AuditEvents.SupportAccessDeclined, "SupportAccessGrant", grant.GrantId.ToString(), new { ticket.TicketId, ticket.ReferenceNumber, note = grant.DecisionNote }, organizationId);
        var assignee = await AdministratorRecipientsAsync(grant.AssignedPlatformAdministratorId, cancellationToken);
        _notifications.Notify(assignee, NotificationTypes.SupportAccessDeclined, "SupportAccessGrant", grant.GrantId.ToString(), await DataAsync(grant, ticket, cancellationToken), organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return await GetAsync(grant.GrantId, cancellationToken);
    }

    public async Task<SupportAccessGrantDto> RevokeAsync(Guid grantId, GrantDecisionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var grant = await LoadVisibleAsync(grantId, cancellationToken);
        var principal = _tenantContext.Principal;
        var allowed = principal.PrincipalType == PrincipalType.PlatformAdministrator || principal.Roles.Contains(SystemRoles.OrganizationAdmin);
        if (!allowed)
        {
            throw new TenantContextException("Only organization administrators or platform administrators can revoke support access.");
        }

        if (grant.Status is not (SupportAccessGrantStatus.Proposed or SupportAccessGrantStatus.Approved or SupportAccessGrantStatus.Active))
        {
            throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, "support_grant_not_revocable");
        }

        var now = _clock.UtcNow;
        grant.Status = SupportAccessGrantStatus.Revoked;
        grant.RevokedAt = now;
        grant.RevokedByType = principal.PrincipalType;
        grant.RevokedById = principal.AdministratorId ?? principal.UserId;
        grant.DecisionNote = Clean(command.Note) ?? grant.DecisionNote;
        await EndSessionsAsync(grant, SupportSessionEndReason.Revoked, cancellationToken);
        var ticket = await TicketOfAsync(grant, cancellationToken);
        ticket.LastActivityAt = now;
        AddTicketEvent(ticket, TicketEventTypes.SupportAccessRevoked, new { grant.GrantId }, Clean(command.Note));
        _audit.Append(AuditEvents.SupportAccessRevoked, "SupportAccessGrant", grant.GrantId.ToString(), new { ticket.TicketId, ticket.ReferenceNumber, note = Clean(command.Note) }, grant.OrganizationId);

        var recipients = principal.PrincipalType == PrincipalType.PlatformAdministrator
            ? await OrganizationAdminsAsync(grant.OrganizationId, cancellationToken)
            : await AdministratorRecipientsAsync(grant.AssignedPlatformAdministratorId, cancellationToken);
        _notifications.Notify(recipients, NotificationTypes.SupportAccessRevoked, "SupportAccessGrant", grant.GrantId.ToString(), await DataAsync(grant, ticket, cancellationToken), grant.OrganizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return await GetAsync(grant.GrantId, cancellationToken);
    }

    public async Task<SupportSessionDto> EnterAsync(Guid grantId, CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        var administratorId = _tenantContext.Principal.AdministratorId!.Value;
        var grant = await LoadVisibleAsync(grantId, cancellationToken);
        var now = _clock.UtcNow;
        await ExpireIfNeededAsync(grant, now, cancellationToken);
        if (grant.AssignedPlatformAdministratorId != administratorId)
        {
            throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, "support_grant_not_assigned_to_you");
        }

        if (!SupportAccessScope.IsUsable(grant.Status, grant.ExpiresAt, now))
        {
            throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, grant.Status == SupportAccessGrantStatus.Proposed ? "support_grant_not_approved" : "support_grant_not_usable");
        }

        // One explicit session at a time per administrator.
        var open = await _context.SupportSessions.AsPlatformScope(_tenantContext).Where(s => s.AdministratorId == administratorId && s.EndedAt == null).ToListAsync(cancellationToken);
        foreach (var previous in open)
        {
            previous.EndedAt = now;
            previous.EndReason = SupportSessionEndReason.Superseded;
            var previousGrant = await _context.SupportAccessGrants.AsPlatformScope(_tenantContext).SingleAsync(g => g.GrantId == previous.GrantId, cancellationToken);
            if (previousGrant.Status == SupportAccessGrantStatus.Active)
            {
                previousGrant.Status = SupportAccessGrantStatus.Approved;
            }
        }

        var session = new SupportSession
        {
            SessionId = Ids.New(),
            GrantId = grant.GrantId,
            OrganizationId = grant.OrganizationId,
            TicketId = grant.TicketId,
            AdministratorId = administratorId,
            StartedAt = now,
        };
        _context.SupportSessions.Add(session);
        grant.Status = SupportAccessGrantStatus.Active;
        var ticket = await TicketOfAsync(grant, cancellationToken);
        ticket.LastActivityAt = now;
        AddTicketEvent(ticket, TicketEventTypes.SupportSessionStarted, new { grant.GrantId, session.SessionId });
        _audit.Append(AuditEvents.SupportSessionStarted, "SupportSession", session.SessionId.ToString(), new { grant.GrantId, ticket.TicketId, ticket.ReferenceNumber }, grant.OrganizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return (await CurrentSessionAsync(cancellationToken))!;
    }

    public async Task ExitAsync(CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        var administratorId = _tenantContext.Principal.AdministratorId!.Value;
        var sessions = await _context.SupportSessions.AsPlatformScope(_tenantContext).Where(s => s.AdministratorId == administratorId && s.EndedAt == null).ToListAsync(cancellationToken);
        var now = _clock.UtcNow;
        foreach (var session in sessions)
        {
            session.EndedAt = now;
            session.EndReason = SupportSessionEndReason.Exit;
            var grant = await _context.SupportAccessGrants.AsPlatformScope(_tenantContext).SingleAsync(g => g.GrantId == session.GrantId, cancellationToken);
            if (grant.Status == SupportAccessGrantStatus.Active)
            {
                grant.Status = SupportAccessGrantStatus.Approved;
            }

            var ticket = await TicketOfAsync(grant, cancellationToken);
            AddTicketEvent(ticket, TicketEventTypes.SupportSessionEnded, new { grant.GrantId, session.SessionId, reason = "Exit", session.OperationsExecuted });
            _audit.Append(AuditEvents.SupportSessionEnded, "SupportSession", session.SessionId.ToString(), new { grant.GrantId, reason = "Exit", session.OperationsExecuted }, grant.OrganizationId);
        }

        if (sessions.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<SupportAccessGrantDto> CompleteAsync(Guid grantId, GrantDecisionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequirePlatformAdministrator();
        var grant = await LoadVisibleAsync(grantId, cancellationToken);
        if (grant.Status is not (SupportAccessGrantStatus.Approved or SupportAccessGrantStatus.Active))
        {
            throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, "support_grant_not_usable");
        }

        var now = _clock.UtcNow;
        grant.Status = SupportAccessGrantStatus.Completed;
        grant.CompletedAt = now;
        grant.DecisionNote = Clean(command.Note) ?? grant.DecisionNote;
        await EndSessionsAsync(grant, SupportSessionEndReason.Completed, cancellationToken);
        var ticket = await TicketOfAsync(grant, cancellationToken);
        ticket.LastActivityAt = now;
        AddTicketEvent(ticket, TicketEventTypes.SupportAccessCompleted, new { grant.GrantId }, Clean(command.Note));
        _audit.Append(AuditEvents.SupportAccessCompleted, "SupportAccessGrant", grant.GrantId.ToString(), new { ticket.TicketId, ticket.ReferenceNumber }, grant.OrganizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return await GetAsync(grant.GrantId, cancellationToken);
    }

    public async Task<SupportSessionDto?> CurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsPlatformAdministrator)
        {
            return null;
        }

        var administratorId = _tenantContext.Principal.AdministratorId!.Value;
        var session = await _context.SupportSessions.AsPlatformScope(_tenantContext).AsNoTracking().SingleOrDefaultAsync(s => s.AdministratorId == administratorId && s.EndedAt == null, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var grant = await _context.SupportAccessGrants.AsPlatformScope(_tenantContext).SingleAsync(g => g.GrantId == session.GrantId, cancellationToken);
        if (await ExpireIfNeededAsync(grant, _clock.UtcNow, cancellationToken) || grant.Status != SupportAccessGrantStatus.Active)
        {
            return null;
        }

        var dto = await GetAsync(grant.GrantId, cancellationToken);
        return new SupportSessionDto(session.SessionId, dto, session.StartedAt, grant.ExpiresAt!.Value, dto.Operations);
    }

    public async Task<PagedResult<SupportAccessGrantDto>> ListAsync(GrantListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var grants = Visible().AsNoTracking();
        if (query.Status is { } status)
        {
            grants = grants.Where(g => g.Status == status);
        }

        if (query.Pending == true)
        {
            grants = grants.Where(g => g.Status == SupportAccessGrantStatus.Proposed);
        }

        if (query.OrganizationId is { } organizationId && _tenantContext.IsPlatformAdministrator)
        {
            grants = grants.Where(g => g.OrganizationId == organizationId);
        }

        if (query.TicketId is { } ticketId)
        {
            grants = grants.Where(g => g.TicketId == ticketId);
        }

        var total = await grants.CountAsync(cancellationToken);
        var items = await grants.OrderByDescending(g => g.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<SupportAccessGrantDto>(await ProjectAsync(items, cancellationToken), page, pageSize, total);
    }

    public async Task<IReadOnlyList<SupportAccessGrantDto>> ListForTicketAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var items = await Visible().AsNoTracking().Where(g => g.TicketId == ticketId).OrderByDescending(g => g.CreatedAt).ToListAsync(cancellationToken);
        return await ProjectAsync(items, cancellationToken);
    }

    public async Task<SupportAccessGrantDto> GetAsync(Guid grantId, CancellationToken cancellationToken = default)
    {
        var grant = await Visible().AsNoTracking().SingleOrDefaultAsync(g => g.GrantId == grantId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "support_grant_not_found");
        return (await ProjectAsync([grant], cancellationToken))[0];
    }

    public async Task<int> ExpireAsync(CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        var now = _clock.UtcNow;
        var due = await _context.SupportAccessGrants.AsPlatformScope(_tenantContext)
            .Where(g => (g.Status == SupportAccessGrantStatus.Approved || g.Status == SupportAccessGrantStatus.Active) && g.ExpiresAt != null && g.ExpiresAt <= now)
            .ToListAsync(cancellationToken);
        foreach (var grant in due)
        {
            await MarkExpiredAsync(grant, now, cancellationToken);
        }

        if (due.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return due.Count;
    }

    // ---- helpers -----------------------------------------------------------------------------

    private IQueryable<SupportAccessGrant> Visible()
    {
        if (_tenantContext.IsPlatformAdministrator)
        {
            return _context.SupportAccessGrants.AsPlatformScope(_tenantContext);
        }

        var userId = _tenantContext.Principal.UserId ?? throw new TenantContextException("Support access requires an authenticated principal.");
        var grants = _context.SupportAccessGrants; // tenant filter
        return _tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin) ? grants : grants.Where(g => g.RequestedByUserId == userId);
    }

    private async Task<SupportAccessGrant> LoadVisibleAsync(Guid grantId, CancellationToken cancellationToken)
        => await Visible().SingleOrDefaultAsync(g => g.GrantId == grantId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "support_grant_not_found");

    private Task<SupportTicket> TicketOfAsync(SupportAccessGrant grant, CancellationToken cancellationToken)
    {
        var tickets = _tenantContext.IsPlatformAdministrator ? _context.SupportTickets.AsPlatformScope(_tenantContext) : _context.SupportTickets;
        return tickets.SingleAsync(t => t.TicketId == grant.TicketId, cancellationToken);
    }

    private void RequirePlatformAdministrator()
    {
        if (!_tenantContext.IsPlatformAdministrator)
        {
            throw new TenantContextException("Only platform administrators can perform this support-access action.");
        }
    }

    private Guid RequireOrganizationAdmin()
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        if (!_tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin))
        {
            throw new TenantContextException("Only organization administrators can decide on support access.");
        }

        return organizationId;
    }

    /// <summary>Every target reference must be an object of the ticket's organization (or "new" for a role to create).</summary>
    private async Task ValidateTargetsAsync(Guid organizationId, IReadOnlyList<SupportOperationEntry> entries, CancellationToken cancellationToken)
    {
        foreach (var entry in entries)
        {
            var definition = SupportOperations.Find(entry.Operation)!;
            foreach (var reference in entry.TargetReferences)
            {
                var ok = definition.TargetType switch
                {
                    SupportTargetType.OrganizationUser => Guid.TryParse(reference, out var u) && await _context.OrganizationUsers.AsPlatformScope(_tenantContext).AnyAsync(x => x.OrganizationId == organizationId && x.UserId == u, cancellationToken),
                    SupportTargetType.Role => Guid.TryParse(reference, out var r) && await _context.Roles.AsPlatformScope(_tenantContext).AnyAsync(x => x.OrganizationId == organizationId && x.RoleId == r, cancellationToken),
                    SupportTargetType.RoleOrNew => string.Equals(reference, SupportAccessScope.NewRoleReference, StringComparison.OrdinalIgnoreCase)
                        || (Guid.TryParse(reference, out var rn) && await _context.Roles.AsPlatformScope(_tenantContext).AnyAsync(x => x.OrganizationId == organizationId && x.RoleId == rn, cancellationToken)),
                    SupportTargetType.Application => Guid.TryParse(reference, out var a) && await _context.Applications.AsPlatformScope(_tenantContext).AnyAsync(x => x.OrganizationId == organizationId && x.ApplicationId == a, cancellationToken),
                    _ => false,
                };
                if (!ok)
                {
                    throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "support_target_not_in_organization");
                }
            }
        }
    }

    private async Task<bool> ExpireIfNeededAsync(SupportAccessGrant grant, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!SupportAccessScope.HasExpired(grant.Status, grant.ExpiresAt, now))
        {
            return false;
        }

        await MarkExpiredAsync(grant, now, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task MarkExpiredAsync(SupportAccessGrant grant, DateTimeOffset now, CancellationToken cancellationToken)
    {
        grant.Status = SupportAccessGrantStatus.Expired;
        await EndSessionsAsync(grant, SupportSessionEndReason.Expired, cancellationToken);
        var ticket = await _context.SupportTickets.AsPlatformScope(_tenantContext).SingleAsync(t => t.TicketId == grant.TicketId, cancellationToken);
        AddTicketEvent(ticket, TicketEventTypes.SupportAccessExpired, new { grant.GrantId, grant.ExpiresAt });
        _audit.Append(AuditEvents.SupportAccessExpired, "SupportAccessGrant", grant.GrantId.ToString(), new { ticket.TicketId, ticket.ReferenceNumber, grant.ExpiresAt }, grant.OrganizationId);
        var assignee = await AdministratorRecipientsAsync(grant.AssignedPlatformAdministratorId, cancellationToken);
        _notifications.Notify(assignee, NotificationTypes.SupportAccessExpired, "SupportAccessGrant", grant.GrantId.ToString(), await DataAsync(grant, ticket, cancellationToken), grant.OrganizationId);
    }

    private async Task EndSessionsAsync(SupportAccessGrant grant, SupportSessionEndReason reason, CancellationToken cancellationToken)
    {
        var sessions = _tenantContext.IsPlatformAdministrator ? _context.SupportSessions.AsPlatformScope(_tenantContext) : _context.SupportSessions;
        var open = await sessions.Where(s => s.GrantId == grant.GrantId && s.EndedAt == null).ToListAsync(cancellationToken);
        var now = _clock.UtcNow;
        foreach (var session in open)
        {
            session.EndedAt = now;
            session.EndReason = reason;
            var ticket = await TicketOfAsync(grant, cancellationToken);
            AddTicketEvent(ticket, TicketEventTypes.SupportSessionEnded, new { grant.GrantId, session.SessionId, reason = reason.ToString(), session.OperationsExecuted });
            _audit.Append(AuditEvents.SupportSessionEnded, "SupportSession", session.SessionId.ToString(), new { grant.GrantId, reason = reason.ToString(), session.OperationsExecuted }, grant.OrganizationId);
        }
    }

    internal void AddTicketEvent(SupportTicket ticket, string eventType, object? details, string? note = null)
    {
        var principal = _tenantContext.Principal;
        _context.SupportTicketEvents.Add(new SupportTicketEvent
        {
            EventId = Ids.New(),
            TicketId = ticket.TicketId,
            OrganizationId = ticket.OrganizationId,
            ActorType = principal.PrincipalType,
            ActorId = principal.AdministratorId ?? principal.UserId,
            ActorDisplayName = principal.DisplayName,
            EventType = eventType,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details, JsonOptions),
            Note = note,
            CreatedAt = _clock.UtcNow,
        });
    }

    private async Task<List<NotificationRecipient>> OrganizationAdminsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var users = _tenantContext.IsPlatformAdministrator ? _context.OrganizationUsers.AsPlatformScope(_tenantContext) : _context.OrganizationUsers;
        return await users.AsNoTracking()
            .Where(u => u.OrganizationId == organizationId && u.Status == OrganizationUserStatus.Active
                && _context.UserRoles.Any(ur => ur.UserId == u.UserId && _context.Roles.Any(r => r.RoleId == ur.RoleId && r.RoleCode == SystemRoles.OrganizationAdmin)))
            .Select(u => new NotificationRecipient(PrincipalType.OrganizationUser, u.UserId, u.OrganizationId, u.Email, u.FullName, u.PreferredLanguage))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<NotificationRecipient>> AdministratorRecipientsAsync(Guid administratorId, CancellationToken cancellationToken)
        => await _context.Administrators.AsNoTracking().Where(a => a.AdministratorId == administratorId && a.IsActive)
            .Select(a => new NotificationRecipient(PrincipalType.PlatformAdministrator, a.AdministratorId, null, a.Email, a.FullName, a.PreferredLanguage))
            .ToListAsync(cancellationToken);

    private async Task<object> DataAsync(SupportAccessGrant grant, SupportTicket ticket, CancellationToken cancellationToken)
    {
        var organization = await _context.Organizations.AsNoTracking().Where(o => o.OrganizationId == grant.OrganizationId).Select(o => new { o.OrganizationName, o.OrganizationNameAr }).SingleAsync(cancellationToken);
        return new { grantId = grant.GrantId, ticketId = ticket.TicketId, reference = ticket.ReferenceNumber, subject = ticket.Subject, organizationName = organization.OrganizationName, organizationNameAr = organization.OrganizationNameAr, actorName = _tenantContext.Principal.DisplayName, newStatus = grant.Status, expiresAt = grant.ExpiresAt };
    }

    private async Task<List<SupportAccessGrantDto>> ProjectAsync(List<SupportAccessGrant> grants, CancellationToken cancellationToken)
    {
        if (grants.Count == 0)
        {
            return [];
        }

        var now = _clock.UtcNow;
        var ticketIds = grants.Select(g => g.TicketId).Distinct().ToList();
        var organizationIds = grants.Select(g => g.OrganizationId).Distinct().ToList();
        var userIds = grants.SelectMany(g => new[] { g.RequestedByUserId, g.ApprovedByOrganizationAdminUserId ?? Guid.Empty }).Distinct().ToList();
        var adminIds = grants.SelectMany(g => new[] { g.ProposedByAdministratorId, g.AssignedPlatformAdministratorId }).Distinct().ToList();
        var grantIds = grants.Select(g => g.GrantId).ToList();

        var tickets = _tenantContext.IsPlatformAdministrator ? _context.SupportTickets.AsPlatformScope(_tenantContext) : _context.SupportTickets;
        var users = _tenantContext.IsPlatformAdministrator ? _context.OrganizationUsers.AsPlatformScope(_tenantContext) : _context.OrganizationUsers;
        var sessionsQuery = _tenantContext.IsPlatformAdministrator ? _context.SupportSessions.AsPlatformScope(_tenantContext) : _context.SupportSessions;

        var ticketMap = await tickets.AsNoTracking().Where(t => ticketIds.Contains(t.TicketId)).Select(t => new { t.TicketId, t.ReferenceNumber, t.Subject }).ToDictionaryAsync(t => t.TicketId, cancellationToken);
        var organizationMap = await _context.Organizations.AsNoTracking().Where(o => organizationIds.Contains(o.OrganizationId)).Select(o => new { o.OrganizationId, o.OrganizationName, o.OrganizationNameAr, o.RequireSecondAdminApprovalForSupportAccess }).ToDictionaryAsync(o => o.OrganizationId, cancellationToken);
        var userMap = await users.AsNoTracking().Where(u => userIds.Contains(u.UserId)).Select(u => new { u.UserId, u.FullName }).ToDictionaryAsync(u => u.UserId, u => u.FullName, cancellationToken);
        var adminMap = await _context.Administrators.AsNoTracking().Where(a => adminIds.Contains(a.AdministratorId)).Select(a => new { a.AdministratorId, a.FullName }).ToDictionaryAsync(a => a.AdministratorId, a => a.FullName, cancellationToken);
        var sessions = await sessionsQuery.AsNoTracking().Where(s => grantIds.Contains(s.GrantId)).Select(s => new { s.GrantId, Active = s.EndedAt == null, s.OperationsExecuted }).ToListAsync(cancellationToken);

        var principal = _tenantContext.Principal;
        return grants.Select(g =>
        {
            var ticket = ticketMap[g.TicketId];
            var organization = organizationMap[g.OrganizationId];
            var operations = JsonSerializer.Deserialize<List<SupportOperationEntry>>(g.AllowedOperationsJson, JsonOptions) ?? [];
            var active = sessions.Any(s => s.GrantId == g.GrantId && s.Active) && g.Status == SupportAccessGrantStatus.Active;
            var executed = sessions.Where(s => s.GrantId == g.GrantId).Sum(s => s.OperationsExecuted);
            var usable = SupportAccessScope.IsUsable(g.Status, g.ExpiresAt, now);
            var actions = new List<string>();
            if (principal.PrincipalType == PrincipalType.PlatformAdministrator)
            {
                if (usable && g.AssignedPlatformAdministratorId == principal.AdministratorId && !active) { actions.Add("enter"); }
                if (active && g.AssignedPlatformAdministratorId == principal.AdministratorId) { actions.Add("exit"); }
                if (g.Status is SupportAccessGrantStatus.Approved or SupportAccessGrantStatus.Active) { actions.Add("complete"); }
                if (g.Status is SupportAccessGrantStatus.Proposed or SupportAccessGrantStatus.Approved or SupportAccessGrantStatus.Active) { actions.Add("revoke"); }
            }
            else if (principal.Roles.Contains(SystemRoles.OrganizationAdmin))
            {
                if (g.Status == SupportAccessGrantStatus.Proposed)
                {
                    if (!(g.RequestedByUserId == principal.UserId && organization.RequireSecondAdminApprovalForSupportAccess)) { actions.Add("approve"); }
                    actions.Add("decline");
                }

                if (g.Status is SupportAccessGrantStatus.Approved or SupportAccessGrantStatus.Active) { actions.Add("revoke"); }
            }

            return new SupportAccessGrantDto(
                g.GrantId, g.TicketId, ticket.ReferenceNumber, ticket.Subject, g.OrganizationId, organization.OrganizationName, organization.OrganizationNameAr,
                g.Status, operations, g.Reason, g.DecisionNote,
                g.RequestedByUserId, userMap.GetValueOrDefault(g.RequestedByUserId, string.Empty),
                g.ProposedByAdministratorId, adminMap.GetValueOrDefault(g.ProposedByAdministratorId, string.Empty),
                g.ApprovedByOrganizationAdminUserId, g.ApprovedByOrganizationAdminUserId is { } a ? userMap.GetValueOrDefault(a) : null,
                g.AssignedPlatformAdministratorId, adminMap.GetValueOrDefault(g.AssignedPlatformAdministratorId, string.Empty),
                g.ApprovedAt, g.ExpiresAt, g.RevokedAt, g.CompletedAt, g.CreatedAt, active, executed, actions);
        }).ToList();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
