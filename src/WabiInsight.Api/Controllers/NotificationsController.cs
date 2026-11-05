using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Core.Notifications;
using WabiInsight.Core.Organizations;

namespace WabiInsight.Api.Controllers;

/// <summary>Notification centre of the signed-in principal (organization user or platform administrator).</summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications)
    {
        _notifications = notifications;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<NotificationDto>>> List([FromQuery] bool unreadOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
        => Ok(await _notifications.ListAsync(unreadOnly, page, pageSize, cancellationToken));

    [HttpGet("unread-count")]
    public async Task<ActionResult<object>> UnreadCount(CancellationToken cancellationToken)
        => Ok(new { count = await _notifications.UnreadCountAsync(cancellationToken) });

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
        => await _notifications.MarkReadAsync(id, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("read-all")]
    public async Task<ActionResult<object>> MarkAllRead(CancellationToken cancellationToken)
        => Ok(new { marked = await _notifications.MarkAllReadAsync(cancellationToken) });
}
