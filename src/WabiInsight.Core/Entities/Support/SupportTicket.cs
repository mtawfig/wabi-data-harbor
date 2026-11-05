using WabiInsight.Core.Common;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Support;

public enum TicketType
{
    Request = 1,
    Incident = 2,
}

public enum TicketImpact
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

/// <summary>Stable status codes (specification §15.3). Labels are localized in the UI.</summary>
public enum TicketStatus
{
    New = 1,
    Read = 2,
    Started = 3,
    PendingUserResponse = 4,
    Cancelled = 5,
    Resolved = 6,
}

/// <summary>A support ticket raised by an organization user and handled by Platform Administrators (specification §15.5).</summary>
public class SupportTicket : AuditableEntity, ITenantOwned
{
    public Guid TicketId { get; set; }

    /// <summary>Human-readable, unique: WI-YYYY-NNNNNN.</summary>
    public string ReferenceNumber { get; set; } = string.Empty;

    public Guid OrganizationId { get; set; }

    public Guid CreatedByUserId { get; set; }

    public TicketType TicketType { get; set; }

    public TicketImpact Impact { get; set; }

    public string? Category { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public TicketStatus Status { get; set; } = TicketStatus.New;

    public Guid? AssignedAdministratorId { get; set; }

    public Guid? FirstOpenedByAdministratorId { get; set; }

    public DateTimeOffset? FirstOpenedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? PendingResponseAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public int ReopenedCount { get; set; }

    public DateTimeOffset LastActivityAt { get; set; }

    public string? ResolutionSummary { get; set; }

    /// <summary>True when resolving needs a scoped support-access grant (Phase 5). A ticket alone grants nothing.</summary>
    public bool RequiresSupportAccess { get; set; }
}

/// <summary>Yearly counter behind ticket references; updated atomically inside the create transaction (review B7).</summary>
public class TicketSequence
{
    public int Year { get; set; }

    public long NextValue { get; set; }
}

/// <summary>Append-only timeline of a ticket (specification §15.10).</summary>
public class SupportTicketEvent : ITenantOwned
{
    public Guid EventId { get; set; }

    public Guid TicketId { get; set; }

    public Guid OrganizationId { get; set; }

    public PrincipalType ActorType { get; set; }

    public Guid? ActorId { get; set; }

    public string ActorDisplayName { get; set; } = string.Empty;

    /// <summary>Stable code from <see cref="TicketEventTypes"/>.</summary>
    public string EventType { get; set; } = string.Empty;

    public TicketStatus? PreviousStatus { get; set; }

    public TicketStatus? NewStatus { get; set; }

    public string? DetailsJson { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public static class TicketEventTypes
{
    public const string Created = "TicketCreated";
    public const string Viewed = "TicketViewed";
    public const string StatusChanged = "StatusChanged";
    public const string Reopened = "TicketReopened";
    public const string Assigned = "TicketAssigned";
    public const string Unassigned = "TicketUnassigned";
    public const string CommentAdded = "CommentAdded";
    public const string AttachmentAdded = "AttachmentAdded";
    public const string SupportAccessFlagChanged = "SupportAccessFlagChanged";
    public const string SupportAccessProposed = "SupportAccessProposed";
    public const string SupportAccessApproved = "SupportAccessApproved";
    public const string SupportAccessDeclined = "SupportAccessDeclined";
    public const string SupportAccessRevoked = "SupportAccessRevoked";
    public const string SupportAccessExpired = "SupportAccessExpired";
    public const string SupportAccessCompleted = "SupportAccessCompleted";
    public const string SupportSessionStarted = "SupportSessionStarted";
    public const string SupportSessionEnded = "SupportSessionEnded";
    public const string SupportOperationExecuted = "SupportOperationExecuted";
}

/// <summary>Conversation entry. Visible to everyone who can see the ticket.</summary>
public class SupportTicketComment : ITenantOwned
{
    public Guid CommentId { get; set; }

    public Guid TicketId { get; set; }

    public Guid OrganizationId { get; set; }

    public PrincipalType AuthorType { get; set; }

    public Guid? AuthorId { get; set; }

    public string AuthorDisplayName { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

public enum AttachmentScanStatus
{
    NotScanned = 0,
    Clean = 1,
    Rejected = 2,
}

/// <summary>File attached to a ticket. Bytes live in <c>IArtifactStorage</c> under a random key; downloads go through the API only.</summary>
public class SupportTicketAttachment : ITenantOwned
{
    public Guid AttachmentId { get; set; }

    public Guid TicketId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid? CommentId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string StorageKey { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public AttachmentScanStatus ScanStatus { get; set; }

    public PrincipalType UploadedByType { get; set; }

    public Guid? UploadedById { get; set; }

    public string UploadedByDisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
