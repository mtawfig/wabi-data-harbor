using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Authorization;
using WabiInsight.Core.Monitoring;
using WabiInsight.Core.Organizations;

namespace WabiInsight.Api.Controllers;

/// <summary>
/// Periodic Reports (specification §35.3). Managing subscriptions is for Organization Administrators (any) and
/// Management users (themselves only), enforced in the service; receiving is for every organization user.
/// </summary>
[ApiController]
[Route("api/subscriptions")]
[Authorize(Policy = Policies.OrganizationUser)]
public sealed class PeriodicReportsController : ControllerBase
{
    private readonly IPeriodicReportService _reports;

    public PeriodicReportsController(IPeriodicReportService reports)
    {
        _reports = reports;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReportSubscriptionDto>>> List(CancellationToken cancellationToken)
        => Ok(await _reports.ListAsync(cancellationToken));

    [HttpGet("analyses")]
    public async Task<ActionResult<IReadOnlyList<SubscriptionAnalysisOption>>> Analyses([FromQuery] Guid? subscriptionId, CancellationToken cancellationToken)
        => Ok(await _reports.ListAnalysesAsync(subscriptionId, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReportSubscriptionDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await _reports.GetAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<ReportSubscriptionDto>> Create([FromBody] SaveReportSubscriptionCommand command, CancellationToken cancellationToken)
    {
        var created = await _reports.CreateAsync(command, cancellationToken);
        return Created($"/api/subscriptions/{created.SubscriptionId}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ReportSubscriptionDto>> Update(Guid id, [FromBody] SaveReportSubscriptionCommand command, CancellationToken cancellationToken)
        => Ok(await _reports.UpdateAsync(id, command, cancellationToken));

    [HttpPost("{id:guid}/pause")]
    public async Task<ActionResult<ReportSubscriptionDto>> Pause(Guid id, CancellationToken cancellationToken)
        => Ok(await _reports.SetActiveAsync(id, false, cancellationToken));

    [HttpPost("{id:guid}/resume")]
    public async Task<ActionResult<ReportSubscriptionDto>> Resume(Guid id, CancellationToken cancellationToken)
        => Ok(await _reports.SetActiveAsync(id, true, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _reports.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Delivers now to every recipient, each under their own permissions; the schedule is unchanged.</summary>
    [HttpPost("{id:guid}/run-now")]
    public async Task<ActionResult<SubscriptionRunResultDto>> RunNow(Guid id, CancellationToken cancellationToken)
        => Ok(await _reports.RunNowAsync(id, cancellationToken));

    [HttpGet("{id:guid}/deliveries")]
    public async Task<ActionResult<PagedResult<ReportDeliveryDto>>> Deliveries(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
        => Ok(await _reports.ListDeliveriesAsync(id, page, pageSize, cancellationToken));

    /// <summary>Periodic reports delivered to the caller ("sent to me").</summary>
    [HttpGet("received")]
    public async Task<ActionResult<PagedResult<ReceivedReportDto>>> Received([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
        => Ok(await _reports.ListReceivedAsync(page, pageSize, cancellationToken));

    [HttpGet("received/{deliveryId:guid}/file")]
    public async Task<IActionResult> ReceivedFile(Guid deliveryId, CancellationToken cancellationToken)
    {
        var download = await _reports.OpenReceivedAsync(deliveryId, cancellationToken);
        if (download is null)
        {
            return NotFound();
        }

        Response.Headers.Append("X-Content-Type-Options", "nosniff");
        return File(download.Content, download.ContentType, download.FileName);
    }
}
