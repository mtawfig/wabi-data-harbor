using WabiInsight.Core.Entities.Support;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Support;

public sealed record CreateTicketCommand(TicketType TicketType, TicketImpact Impact, string? Category, string Subject, string Description, bool RequiresSupportAccess = false);

public sealed record TicketListQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    TicketStatus? Status = null,
    bool? Open = null,
    TicketType? TicketType = null,
    TicketImpact? Impact = null,
    Guid? OrganizationId = null,
    Guid? AssignedAdministratorId = null,
    bool? Unassigned = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Sort = null,
    bool Descending = true);

public sealed record TicketSummaryDto(
    Guid TicketId,
    string ReferenceNumber,
    Guid OrganizationId,
    string OrganizationCode,
    string OrganizationName,
    string OrganizationNameAr,
    TicketType TicketType,
    TicketImpact Impact,
    string? Category,
    string Subject,
    TicketStatus Status,
    Guid CreatedByUserId,
    string CreatedByName,
    Guid? AssignedAdministratorId,
    string? AssignedAdministratorName,
    bool RequiresSupportAccess,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    int CommentCount,
    int AttachmentCount);

public sealed record TicketDetailDto(
    TicketSummaryDto Summary,
    string Description,
    Guid? FirstOpenedByAdministratorId,
    DateTimeOffset? FirstOpenedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? PendingResponseAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? CancelledAt,
    int ReopenedCount,
    string? ResolutionSummary,
    DateTimeOffset? UpdatedAt,
    /// <summary>What the current principal may do, so the UI only offers valid actions.</summary>
    IReadOnlyList<TicketStatus> AllowedTransitions,
    bool CanComment,
    bool CanAssign);

public sealed record TicketEventDto(Guid EventId, PrincipalType ActorType, Guid? ActorId, string ActorDisplayName, string EventType, TicketStatus? PreviousStatus, TicketStatus? NewStatus, string? DetailsJson, string? Note, DateTimeOffset CreatedAt);

public sealed record TicketCommentDto(Guid CommentId, PrincipalType AuthorType, Guid? AuthorId, string AuthorDisplayName, string Body, DateTimeOffset CreatedAt);

public sealed record TicketAttachmentDto(Guid AttachmentId, Guid? CommentId, string FileName, string ContentType, long SizeBytes, PrincipalType UploadedByType, string UploadedByDisplayName, DateTimeOffset CreatedAt);

public sealed record ChangeTicketStatusCommand(TicketStatus Status, string? Note);

public sealed record AttachmentUpload(string FileName, string? ContentType, long Length, Stream Content);

public sealed record AttachmentDownload(TicketAttachmentDto Attachment, Stream Content);

/// <summary>
/// Support desk (specification §15). Visibility: requester sees own tickets, OrganizationAdmin sees the
/// organization's, Platform Administrators see every ticket. Every meaningful action is stored on the timeline.
/// </summary>
public interface ISupportTicketService
{
    Task<TicketDetailDto> CreateAsync(CreateTicketCommand command, CancellationToken cancellationToken = default);

    Task<PagedResult<TicketSummaryDto>> ListAsync(TicketListQuery query, CancellationToken cancellationToken = default);

    /// <summary>Returns the ticket, recording the first platform view (NEW → READ) exactly once.</summary>
    Task<TicketDetailDto> GetAsync(Guid ticketId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TicketEventDto>> ListEventsAsync(Guid ticketId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TicketCommentDto>> ListCommentsAsync(Guid ticketId, CancellationToken cancellationToken = default);

    Task<TicketCommentDto> AddCommentAsync(Guid ticketId, string body, CancellationToken cancellationToken = default);

    Task<TicketDetailDto> ChangeStatusAsync(Guid ticketId, ChangeTicketStatusCommand command, CancellationToken cancellationToken = default);

    /// <summary>Platform administrators only. Null unassigns.</summary>
    Task<TicketDetailDto> AssignAsync(Guid ticketId, Guid? administratorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TicketAttachmentDto>> ListAttachmentsAsync(Guid ticketId, CancellationToken cancellationToken = default);

    Task<TicketAttachmentDto> AddAttachmentAsync(Guid ticketId, AttachmentUpload upload, CancellationToken cancellationToken = default);

    Task<AttachmentDownload?> OpenAttachmentAsync(Guid ticketId, Guid attachmentId, CancellationToken cancellationToken = default);
}

/// <summary>Support desk settings, bound from the "SupportDesk" section.</summary>
public sealed class SupportDeskOptions
{
    public const string SectionName = "SupportDesk";

    public static readonly IReadOnlyList<string> DefaultAllowedExtensions = ["pdf", "png", "jpg", "jpeg", "xlsx", "docx", "csv", "txt", "log", "zip"];

    public int ReopenWindowDays { get; set; } = TicketStateMachine.DefaultReopenWindowDays;

    public long MaxAttachmentBytes { get; set; } = 10L * 1024 * 1024;

    public IList<string> AllowedExtensions { get; } = [.. DefaultAllowedExtensions];
}
