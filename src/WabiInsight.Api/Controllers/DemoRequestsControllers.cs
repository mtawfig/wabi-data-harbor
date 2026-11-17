using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WabiInsight.Api.Authorization;
using WabiInsight.Api.Security;
using WabiInsight.Core.Demo;
using WabiInsight.Core.Organizations;

namespace WabiInsight.Api.Controllers;

/// <summary>What the public form sends. The limits here stop oversized input early; the service applies the real rules.</summary>
public sealed record SubmitDemoRequestRequest(
    [MaxLength(400)] string? FullName,
    [MaxLength(400)] string? Email,
    [MaxLength(400)] string? OrganizationName,
    [MaxLength(8)] string? CountryCode,
    bool Consent,
    [MaxLength(400)] string? JobTitle,
    [MaxLength(100)] string? Phone,
    [MaxLength(60)] string? Industry,
    [MaxLength(40)] string? OrganizationSize,
    [MaxLength(400)] string? DatabaseSystems,
    [MaxLength(8)] string? DemoLanguage,
    [MaxLength(400)] string? PreferredTime,
    [MaxLength(4000)] string? Message,
    [MaxLength(8)] string? PageLanguage,
    [MaxLength(400)] string? Website);

public sealed record UpdateDemoRequestRequest(
    [Required, MaxLength(20)] string Status,
    [MaxLength(2000)] string? Remark,
    Guid? AssignedAdministratorId,
    DateTimeOffset? ScheduledAt,
    [MaxLength(400)] string? MeetingLocation,
    DateTimeOffset? PerformedAt,
    [MaxLength(150)] string? PresentedBy,
    [MaxLength(150)] string? AttendeeName,
    [MaxLength(150)] string? AttendeePosition,
    [MaxLength(254)] string? AttendeeEmail,
    [MaxLength(40)] string? AttendeePhone,
    [MaxLength(40)] string? AttendeeMobile,
    [MaxLength(20)] string? Impression,
    [MaxLength(2000)] string? PerformedRemark,
    [MaxLength(500)] string? NextStep,
    DateOnly? FollowUpDate,
    [MaxLength(2000)] string? Note);

public sealed record DemoRequestAvailability(bool Enabled);

/// <summary>The "Request a demo" form of the landing page (P13.52): no account, one endpoint, limited per client address.</summary>
[ApiController]
[Route("api/public/demo-requests")]
[AllowAnonymous]
public sealed class PublicDemoRequestsController : ControllerBase
{
    /// <summary>Every accepted submission takes at least this long, so a stored, repeated or dropped one cannot be told apart by timing.</summary>
    internal static readonly TimeSpan Floor = TimeSpan.FromMilliseconds(400);

    private readonly IDemoRequestService _demoRequests;
    private readonly DemoRequestOptions _options;

    public PublicDemoRequestsController(IDemoRequestService demoRequests, Microsoft.Extensions.Options.IOptions<DemoRequestOptions> options)
    {
        _demoRequests = demoRequests;
        _options = options.Value;
    }

    /// <summary>Whether the form is open: the landing page shows its buttons, and the form page opens, only when it is.</summary>
    [HttpGet("availability")]
    [ProducesResponseType<DemoRequestAvailability>(StatusCodes.Status200OK)]
    public ActionResult<DemoRequestAvailability> Availability() => Ok(new DemoRequestAvailability(_options.Enabled));

    /// <summary>
    /// 204 when the form is complete, whether the request was stored, counted on an earlier one of the same address or
    /// dropped; 400 with the field and a reason code when something is missing or not acceptable.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitingSetup.DemoRequestPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Submit([FromBody] SubmitDemoRequestRequest request, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            // The form is switched off: nothing is stored, e-mailed or notified, whoever sends it.
            return NotFound();
        }

        var floor = Task.Delay(Floor, cancellationToken);
        var result = await _demoRequests.SubmitAsync(
            new SubmitDemoRequestCommand(
                request.FullName ?? string.Empty, request.Email ?? string.Empty, request.OrganizationName ?? string.Empty, request.CountryCode ?? string.Empty,
                request.Consent, request.JobTitle, request.Phone, request.Industry, request.OrganizationSize, request.DatabaseSystems, request.DemoLanguage,
                request.PreferredTime, request.Message, request.PageLanguage, request.Website),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            cancellationToken);
        if (result.Outcome == DemoRequestSubmitOutcome.Invalid)
        {
            foreach (var (field, code) in result.Errors)
            {
                ModelState.AddModelError(field, code);
            }

            return ValidationProblem(ModelState);
        }

        await floor;
        return NoContent();
    }
}

/// <summary>Demo requests for the people who follow them up: Platform Administrators only.</summary>
[ApiController]
[Route("api/platform/demo-requests")]
[Authorize(Policy = Policies.PlatformAdministrator)]
public sealed class PlatformDemoRequestsController : ControllerBase
{
    private readonly IDemoRequestService _demoRequests;

    public PlatformDemoRequestsController(IDemoRequestService demoRequests)
    {
        _demoRequests = demoRequests;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<DemoRequestListItem>>> List(
        [FromQuery] string? status, [FromQuery] string? country, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
        => Ok(await _demoRequests.ListAsync(new DemoRequestListQuery(status, country, from, to, search, page, pageSize), cancellationToken));

    [HttpGet("summary")]
    public async Task<ActionResult<DemoRequestSummary>> Summary(CancellationToken cancellationToken) => Ok(await _demoRequests.SummaryAsync(cancellationToken));

    [HttpGet("assignees")]
    public async Task<ActionResult<IReadOnlyList<DemoAssigneeDto>>> Assignees(CancellationToken cancellationToken) => Ok(await _demoRequests.AssigneesAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DemoRequestDetail>> Get(Guid id, CancellationToken cancellationToken)
        => await _demoRequests.GetAsync(id, cancellationToken) is { } detail ? Ok(detail) : NotFound();

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DemoRequestDetail>> Update(Guid id, [FromBody] UpdateDemoRequestRequest r, CancellationToken cancellationToken)
        => Ok(await _demoRequests.UpdateAsync(id, new UpdateDemoRequestCommand(
            r.Status, r.Remark, r.AssignedAdministratorId, r.ScheduledAt, r.MeetingLocation, r.PerformedAt, r.PresentedBy, r.AttendeeName, r.AttendeePosition,
            r.AttendeeEmail, r.AttendeePhone, r.AttendeeMobile, r.Impression, r.PerformedRemark, r.NextStep, r.FollowUpDate, r.Note), cancellationToken));
}
