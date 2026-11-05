using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Common;
using WabiInsight.Core.Email;
using WabiInsight.Core.Entities.Support;
using WabiInsight.Core.Notifications;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Authentication;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Notifications;

/// <summary>Reads and marks the signed-in principal's notifications. The recipient filter is always the current principal.</summary>
public sealed class NotificationService : INotificationService
{
    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public NotificationService(ConfigurationDbContext context, ITenantContext tenantContext, IClock clock)
    {
        _context = context;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<PagedResult<NotificationDto>> ListAsync(bool unreadOnly, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = Mine().AsNoTracking();
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(n => n.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(n => new NotificationDto(n.NotificationId, n.NotificationType, n.EntityType, n.EntityId, n.DataJson, n.ReadAt, n.CreatedAt))
            .ToListAsync(cancellationToken);
        return new PagedResult<NotificationDto>(items, page, pageSize, total);
    }

    public Task<int> UnreadCountAsync(CancellationToken cancellationToken = default)
        => Mine().CountAsync(n => n.ReadAt == null, cancellationToken);

    public async Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await Mine().SingleOrDefaultAsync(n => n.NotificationId == notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        notification.ReadAt ??= _clock.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        return await Mine().Where(n => n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), cancellationToken);
    }

    private IQueryable<Notification> Mine()
    {
        var principal = _tenantContext.Principal;
        var id = principal.AdministratorId ?? principal.UserId ?? throw new TenantContextException("Notifications require an authenticated principal.");
        var type = principal.PrincipalType;
        return _context.Notifications.Where(n => n.RecipientType == type && n.RecipientId == id);
    }
}

public sealed class NotificationDispatcher : INotificationDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private readonly ConfigurationDbContext _context;
    private readonly IEmailOutbox _outbox;
    private readonly IClock _clock;
    private readonly PlatformUrlOptions _urls;
    private readonly EmailOptions _email;

    public NotificationDispatcher(ConfigurationDbContext context, IEmailOutbox outbox, IClock clock, IOptions<PlatformUrlOptions> urls, IOptions<EmailOptions> email)
    {
        _context = context;
        _outbox = outbox;
        _clock = clock;
        _urls = urls.Value;
        _email = email.Value;
    }

    public void Notify(IEnumerable<NotificationRecipient> recipients, string notificationType, string entityType, string entityId, object data, Guid? organizationId)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var now = _clock.UtcNow;
        foreach (var recipient in recipients)
        {
            _context.Notifications.Add(new Notification
            {
                NotificationId = Ids.New(),
                RecipientType = recipient.RecipientType,
                RecipientId = recipient.RecipientId,
                OrganizationId = recipient.OrganizationId,
                NotificationType = notificationType,
                EntityType = entityType,
                EntityId = entityId,
                DataJson = json,
                CreatedAt = now,
            });
        }
    }

    public async Task EmailAsync(IEnumerable<NotificationRecipient> recipients, string templateCode, string subjectEn, string subjectAr, string bodyEn, string bodyAr, string linkPath, Guid? organizationId, CancellationToken cancellationToken = default)
    {
        var link = $"{_urls.BaseUrl.TrimEnd('/')}/{linkPath.TrimStart('/')}";
        foreach (var recipient in recipients)
        {
            var message = EmailTemplates.Notification(recipient.Email, recipient.FullName, recipient.Language, subjectEn, subjectAr, bodyEn, bodyAr, link, _email.ReplyTo);
            await _outbox.EnqueueAsync(message, templateCode, organizationId, cancellationToken);
        }
    }
}
