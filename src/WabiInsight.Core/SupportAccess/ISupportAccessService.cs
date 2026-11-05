using WabiInsight.Core.Entities.Support;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.SupportAccess;

public sealed record ProposeGrantCommand(IReadOnlyList<SupportOperationEntry> Operations, string Reason, int? ExpiryHours);

public sealed record GrantDecisionCommand(string? Note, int? ExpiryHours = null);

public sealed record GrantListQuery(int Page = 1, int PageSize = 25, SupportAccessGrantStatus? Status = null, Guid? OrganizationId = null, Guid? TicketId = null, bool? Pending = null);

public sealed record SupportAccessGrantDto(
    Guid GrantId,
    Guid TicketId,
    string TicketReference,
    string TicketSubject,
    Guid OrganizationId,
    string OrganizationName,
    string OrganizationNameAr,
    SupportAccessGrantStatus Status,
    IReadOnlyList<SupportOperationEntry> Operations,
    /// <summary>Display labels for target references (user name and email, role name, "new"); identity only.</summary>
    IReadOnlyDictionary<string, string> TargetLabels,
    string Reason,
    string? DecisionNote,
    Guid RequestedByUserId,
    string RequestedByName,
    Guid ProposedByAdministratorId,
    string ProposedByName,
    Guid? ApprovedByOrganizationAdminUserId,
    string? ApprovedByName,
    Guid AssignedPlatformAdministratorId,
    string AssignedAdministratorName,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    bool SessionActive,
    int OperationsExecuted,
    /// <summary>What the current principal may do with this grant, so the UI only offers valid actions.</summary>
    IReadOnlyList<string> AllowedActions);

/// <summary>Objects a proposal may target inside the ticket's organization: identity only, never business data.</summary>
public sealed record SupportTargetUser(Guid UserId, string FullName, string Email, bool IsActive);

public sealed record SupportTargetRole(Guid RoleId, string RoleCode, string NameEn, string NameAr, bool IsSystem, bool IsActive);

public sealed record SupportTargetsDto(IReadOnlyList<SupportTargetUser> Users, IReadOnlyList<SupportTargetRole> Roles);

public sealed record SupportSessionDto(Guid SessionId, SupportAccessGrantDto Grant, DateTimeOffset StartedAt, DateTimeOffset ExpiresAt, IReadOnlyList<SupportOperationEntry> Operations);

/// <summary>Grant lifecycle (specification §16.1): propose → approve/decline → enter/exit support mode → complete, revoke, expire.</summary>
public interface ISupportAccessService
{
    IReadOnlyList<SupportOperationDefinition> Catalog { get; }

    /// <summary>Platform administrators only: users and roles of the ticket's organization that a proposal may target.</summary>
    Task<SupportTargetsDto> ListTargetsAsync(Guid ticketId, CancellationToken cancellationToken = default);

    /// <summary>Platform administrators only. Marks the ticket as requiring support access and notifies the organization's administrators.</summary>
    Task<SupportAccessGrantDto> ProposeAsync(Guid ticketId, ProposeGrantCommand command, CancellationToken cancellationToken = default);

    /// <summary>OrganizationAdmin of the grant's organization. Honors RequireSecondAdminApprovalForSupportAccess.</summary>
    Task<SupportAccessGrantDto> ApproveAsync(Guid grantId, GrantDecisionCommand command, CancellationToken cancellationToken = default);

    Task<SupportAccessGrantDto> DeclineAsync(Guid grantId, GrantDecisionCommand command, CancellationToken cancellationToken = default);

    /// <summary>Any OrganizationAdmin of the organization or any Platform Administrator; ends an active session immediately.</summary>
    Task<SupportAccessGrantDto> RevokeAsync(Guid grantId, GrantDecisionCommand command, CancellationToken cancellationToken = default);

    /// <summary>The assigned administrator enters support mode; any other session of theirs is ended first.</summary>
    Task<SupportSessionDto> EnterAsync(Guid grantId, CancellationToken cancellationToken = default);

    Task ExitAsync(CancellationToken cancellationToken = default);

    Task<SupportAccessGrantDto> CompleteAsync(Guid grantId, GrantDecisionCommand command, CancellationToken cancellationToken = default);

    Task<SupportSessionDto?> CurrentSessionAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<SupportAccessGrantDto>> ListAsync(GrantListQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SupportAccessGrantDto>> ListForTicketAsync(Guid ticketId, CancellationToken cancellationToken = default);

    Task<SupportAccessGrantDto> GetAsync(Guid grantId, CancellationToken cancellationToken = default);

    /// <summary>Marks Approved/Active grants past their expiry as Expired and ends their sessions. Returns the number expired.</summary>
    Task<int> ExpireAsync(CancellationToken cancellationToken = default);
}

/// <summary>Context of an authorized support operation handed to the executor and written to every audit record.</summary>
public sealed record SupportExecutionContext(Guid GrantId, Guid SessionId, Guid TicketId, Guid OrganizationId, Guid AdministratorId, string Operation, string? TargetReference);

/// <summary>
/// The dedicated authorization handler (specification §16.3): resolves the caller's active session and grant
/// and checks scope and expiry for one operation. Throws <see cref="SupportAccessDeniedException"/> otherwise.
/// </summary>
public interface ISupportAccessAuthorizer
{
    Task<bool> HasActiveSessionAsync(CancellationToken cancellationToken = default);

    Task<SupportExecutionContext> AuthorizeAsync(string operation, string? targetReference, CancellationToken cancellationToken = default);
}

public sealed record ExecuteOperationCommand(string Operation, string? TargetReference, System.Text.Json.JsonElement? Payload);

public sealed record SupportOperationResult(string Operation, string? TargetReference, string Outcome, object? Result, string? DeliveredTo = null);

/// <summary>Executes a permitted operation inside the organization of the active grant and records it on the ticket timeline and audit.</summary>
public interface ISupportOperationExecutor
{
    Task<SupportOperationResult> ExecuteAsync(ExecuteOperationCommand command, CancellationToken cancellationToken = default);
}

public sealed class SupportAccessDeniedException : Exception
{
    public SupportAccessDeniedException()
    {
    }

    public SupportAccessDeniedException(string message)
        : base(message)
    {
    }

    public SupportAccessDeniedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SupportAccessDeniedException(string code, bool isCode)
        : base(code)
    {
        Code = isCode ? code : "support_access_denied";
    }

    public string Code { get; } = "support_access_denied";
}

/// <summary>Support access settings, bound from the "SupportAccess" section.</summary>
public sealed class SupportAccessOptions
{
    public const string SectionName = "SupportAccess";

    public int DefaultExpiryHours { get; set; } = SupportAccessScope.DefaultExpiryHours;

    public int MaxExpiryHours { get; set; } = SupportAccessScope.MaxExpiryHours;
}
