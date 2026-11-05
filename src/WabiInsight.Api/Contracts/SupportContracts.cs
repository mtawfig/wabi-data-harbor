using System.ComponentModel.DataAnnotations;
using WabiInsight.Core.Entities.Support;

namespace WabiInsight.Api.Contracts;

public sealed record CreateTicketRequest(
    [Required] TicketType TicketType,
    [Required] TicketImpact Impact,
    [MaxLength(100)] string? Category,
    [Required, MinLength(3), MaxLength(200)] string Subject,
    [Required, MaxLength(8000)] string Description,
    bool RequiresSupportAccess = false);

public sealed record AddTicketCommentRequest([Required, MaxLength(8000)] string Body);

public sealed record ChangeTicketStatusRequest([Required] TicketStatus Status, [MaxLength(4000)] string? Note);

public sealed record AssignTicketRequest(Guid? AdministratorId);
