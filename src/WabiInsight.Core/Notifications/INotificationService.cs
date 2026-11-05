using WabiInsight.Core.Organizations;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Notifications;

public sealed record NotificationDto(Guid NotificationId, string NotificationType, string? EntityType, string? EntityId, string? DataJson, DateTimeOffset? ReadAt, DateTimeOffset CreatedAt);

/// <summary>Notification centre of the signed-in principal. Nobody can read or mark another principal's notifications.</summary>
public interface INotificationService
{
    Task<PagedResult<NotificationDto>> ListAsync(bool unreadOnly, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<int> UnreadCountAsync(CancellationToken cancellationToken = default);

    Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default);

    Task<int> MarkAllReadAsync(CancellationToken cancellationToken = default);
}

public sealed record NotificationRecipient(PrincipalType RecipientType, Guid RecipientId, Guid? OrganizationId, string Email, string FullName, string Language);

/// <summary>Creates in-app notifications (and optional emails) for a set of recipients inside the caller's unit of work.</summary>
public interface INotificationDispatcher
{
    /// <summary>Adds notification rows; the caller's SaveChanges persists them. Email, when requested, is queued after save.</summary>
    void Notify(IEnumerable<NotificationRecipient> recipients, string notificationType, string entityType, string entityId, object data, Guid? organizationId);

    /// <summary>Queues the bilingual email for the recipients (call after the business change is saved).</summary>
    Task EmailAsync(IEnumerable<NotificationRecipient> recipients, string templateCode, string subjectEn, string subjectAr, string bodyEn, string bodyAr, string linkPath, Guid? organizationId, CancellationToken cancellationToken = default);
}
