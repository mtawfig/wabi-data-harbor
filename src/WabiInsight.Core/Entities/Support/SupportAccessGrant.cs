using WabiInsight.Core.Common;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Support;

/// <summary>Stable grant status codes (specification §16.2; Declined added for a rejected proposal).</summary>
public enum SupportAccessGrantStatus
{
    Proposed = 1,
    Approved = 2,
    Active = 3,
    Expired = 4,
    Revoked = 5,
    Completed = 6,
    Declined = 7,
}

public enum SupportAccessScopeType
{
    /// <summary>Only the listed operations against the listed object references.</summary>
    Operations = 1,
}

/// <summary>
/// A scoped, time-limited permission for one Platform Administrator to perform listed configuration
/// operations inside one organization on behalf of one ticket (specification §16). A ticket alone grants nothing.
/// </summary>
public class SupportAccessGrant : AuditableEntity, ITenantOwned
{
    public Guid GrantId { get; set; }

    public Guid TicketId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid RequestedByUserId { get; set; }

    public Guid ProposedByAdministratorId { get; set; }

    public Guid? ApprovedByOrganizationAdminUserId { get; set; }

    public Guid AssignedPlatformAdministratorId { get; set; }

    public SupportAccessScopeType ScopeType { get; set; } = SupportAccessScopeType.Operations;

    /// <summary>JSON array of <see cref="SupportOperationEntry"/>: operation code plus the approved target references.</summary>
    public string AllowedOperationsJson { get; set; } = "[]";

    /// <summary>Comma-separated approved object references (user, role, application ids) — the union of all entries' targets.</summary>
    public string? ApprovedObjectReferences { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string? DecisionNote { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public PrincipalType? RevokedByType { get; set; }

    public Guid? RevokedById { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public SupportAccessGrantStatus Status { get; set; } = SupportAccessGrantStatus.Proposed;
}

/// <summary>One operation of a grant with the specific targets it may touch.</summary>
public sealed record SupportOperationEntry(string Operation, IReadOnlyList<string> TargetReferences);

public enum SupportSessionEndReason
{
    Exit = 1,
    Expired = 2,
    Revoked = 3,
    Completed = 4,
    Superseded = 5,
}

/// <summary>Explicit support-mode session: one administrator, one organization, one ticket, one grant (specification §16.3).</summary>
public class SupportSession : ITenantOwned
{
    public Guid SessionId { get; set; }

    public Guid GrantId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid TicketId { get; set; }

    public Guid AdministratorId { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public SupportSessionEndReason? EndReason { get; set; }

    public int OperationsExecuted { get; set; }
}
