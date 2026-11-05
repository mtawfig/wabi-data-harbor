using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Support;

/// <summary>
/// In-app notification for one principal (organization user or platform administrator). Carries codes and
/// parameters; the UI localizes the text. Organization users' notifications carry their OrganizationId.
/// </summary>
public class Notification
{
    public Guid NotificationId { get; set; }

    public PrincipalType RecipientType { get; set; }

    public Guid RecipientId { get; set; }

    public Guid? OrganizationId { get; set; }

    /// <summary>Stable code from <see cref="NotificationTypes"/>.</summary>
    public string NotificationType { get; set; } = string.Empty;

    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    /// <summary>Parameters for the localized message (ticket reference, subject, actor name, status codes).</summary>
    public string? DataJson { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public static class NotificationTypes
{
    public const string TicketCreated = "Ticket.Created";
    public const string TicketStatusChanged = "Ticket.StatusChanged";
    public const string TicketAssigned = "Ticket.Assigned";
    public const string TicketCommentAdded = "Ticket.CommentAdded";
    public const string TicketAttachmentAdded = "Ticket.AttachmentAdded";
}
