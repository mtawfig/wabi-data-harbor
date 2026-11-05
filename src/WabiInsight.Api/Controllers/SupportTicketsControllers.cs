using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Authorization;
using WabiInsight.Api.Contracts;
using WabiInsight.Core.Entities.Support;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Support;

namespace WabiInsight.Api.Controllers;

/// <summary>
/// Shared ticket actions. The service resolves who the caller is from the tenant context, so the same
/// endpoints serve the organization area (/api/support/tickets) and the platform queue (/api/platform/support/tickets).
/// </summary>
[ApiController]
public abstract class SupportTicketsControllerBase : ControllerBase
{
    private const long MaxUploadBytes = 11L * 1024 * 1024; // 10 MB file + multipart overhead

    protected SupportTicketsControllerBase(ISupportTicketService tickets)
    {
        Tickets = tickets;
    }

    protected ISupportTicketService Tickets { get; }

    [HttpGet]
    public async Task<ActionResult<PagedResult<TicketSummaryDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] TicketStatus? status = null,
        [FromQuery] bool? open = null,
        [FromQuery] TicketType? ticketType = null,
        [FromQuery] TicketImpact? impact = null,
        [FromQuery] Guid? organizationId = null,
        [FromQuery] Guid? assignedAdministratorId = null,
        [FromQuery] bool? unassigned = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? sort = null,
        [FromQuery] bool descending = true,
        CancellationToken cancellationToken = default)
        => Ok(await Tickets.ListAsync(new TicketListQuery(page, pageSize, search, status, open, ticketType, impact, organizationId, assignedAdministratorId, unassigned, from, to, sort, descending), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TicketDetailDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await Tickets.GetAsync(id, cancellationToken));

    [HttpGet("{id:guid}/events")]
    public async Task<ActionResult<IReadOnlyList<TicketEventDto>>> Events(Guid id, CancellationToken cancellationToken)
        => Ok(await Tickets.ListEventsAsync(id, cancellationToken));

    [HttpGet("{id:guid}/comments")]
    public async Task<ActionResult<IReadOnlyList<TicketCommentDto>>> Comments(Guid id, CancellationToken cancellationToken)
        => Ok(await Tickets.ListCommentsAsync(id, cancellationToken));

    [HttpPost("{id:guid}/comments")]
    public async Task<ActionResult<TicketCommentDto>> AddComment(Guid id, [FromBody] AddTicketCommentRequest request, CancellationToken cancellationToken)
    {
        var comment = await Tickets.AddCommentAsync(id, request.Body, cancellationToken);
        return Created($"{Request.Path}/{comment.CommentId}", comment);
    }

    [HttpPost("{id:guid}/status")]
    public async Task<ActionResult<TicketDetailDto>> ChangeStatus(Guid id, [FromBody] ChangeTicketStatusRequest request, CancellationToken cancellationToken)
        => Ok(await Tickets.ChangeStatusAsync(id, new ChangeTicketStatusCommand(request.Status, request.Note), cancellationToken));

    [HttpGet("{id:guid}/attachments")]
    public async Task<ActionResult<IReadOnlyList<TicketAttachmentDto>>> Attachments(Guid id, CancellationToken cancellationToken)
        => Ok(await Tickets.ListAttachmentsAsync(id, cancellationToken));

    [HttpPost("{id:guid}/attachments")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<TicketAttachmentDto>> Upload(Guid id, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return ValidationProblem("A file is required.");
        }

        await using var content = file.OpenReadStream();
        var attachment = await Tickets.AddAttachmentAsync(id, new AttachmentUpload(file.FileName, file.ContentType, file.Length, content), cancellationToken);
        return Created($"{Request.Path}/{attachment.AttachmentId}", attachment);
    }

    [HttpGet("{id:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> Download(Guid id, Guid attachmentId, CancellationToken cancellationToken)
    {
        var download = await Tickets.OpenAttachmentAsync(id, attachmentId, cancellationToken);
        if (download is null)
        {
            return NotFound();
        }

        Response.Headers.Append("X-Content-Type-Options", "nosniff");
        return File(download.Content, download.Attachment.ContentType, download.Attachment.FileName);
    }
}

/// <summary>Organization area: requesters see their own tickets, Organization Administrators all of the organization's.</summary>
[Route("api/support/tickets")]
[Authorize(Policy = Policies.OrganizationUser)]
public sealed class SupportTicketsController : SupportTicketsControllerBase
{
    public SupportTicketsController(ISupportTicketService tickets)
        : base(tickets)
    {
    }

    [HttpPost]
    public async Task<ActionResult<TicketDetailDto>> Create([FromBody] CreateTicketRequest request, CancellationToken cancellationToken)
    {
        var ticket = await Tickets.CreateAsync(new CreateTicketCommand(request.TicketType, request.Impact, request.Category, request.Subject, request.Description, request.RequiresSupportAccess), cancellationToken);
        return Created($"/api/support/tickets/{ticket.Summary.TicketId}", ticket);
    }
}

/// <summary>Platform queue: every organization's tickets, assignment and the first-open rule.</summary>
[Route("api/platform/support/tickets")]
[Authorize(Policy = Policies.PlatformAdministrator)]
public sealed class PlatformSupportTicketsController : SupportTicketsControllerBase
{
    public PlatformSupportTicketsController(ISupportTicketService tickets)
        : base(tickets)
    {
    }

    [HttpPost("{id:guid}/assign")]
    public async Task<ActionResult<TicketDetailDto>> Assign(Guid id, [FromBody] AssignTicketRequest request, CancellationToken cancellationToken)
        => Ok(await Tickets.AssignAsync(id, request.AdministratorId, cancellationToken));
}
