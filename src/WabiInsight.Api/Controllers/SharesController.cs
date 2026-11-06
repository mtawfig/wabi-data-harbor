using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Authorization;
using WabiInsight.Api.Contracts;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Analysis;
using WabiInsight.Core.Organizations;

namespace WabiInsight.Api.Controllers;

/// <summary>
/// Sharing (specification §24): owners share their analyses with colleagues (VIEW, expiry, revocation) or send a
/// version by email under the classification rules; recipients read through Shared With Me only.
/// </summary>
[ApiController]
[Route("api/shares")]
[Authorize(Policy = Policies.OrganizationUser)]
public sealed class SharesController : ControllerBase
{
    private readonly ISharingService _sharing;

    public SharesController(ISharingService sharing)
    {
        _sharing = sharing;
    }

    /// <summary>Active colleagues of the caller's organization (name, email, department) for the share dialog.</summary>
    [HttpGet("recipients")]
    public async Task<ActionResult<IReadOnlyList<ShareRecipientDto>>> Recipients([FromQuery] string? search, CancellationToken cancellationToken)
        => Ok(await _sharing.SearchRecipientsAsync(search, cancellationToken));

    [HttpGet("by-me")]
    public async Task<ActionResult<PagedResult<ShareDto>>> ByMe([FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] string? search = null, [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default)
        => Ok(await _sharing.ListSharedByMeAsync(page, pageSize, search, includeInactive, cancellationToken));

    [HttpGet("with-me")]
    public async Task<ActionResult<PagedResult<SharedAnalysisDto>>> WithMe([FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] string? search = null, CancellationToken cancellationToken = default)
        => Ok(await _sharing.ListSharedWithMeAsync(page, pageSize, search, cancellationToken));

    [HttpGet("with-me/{historyId:guid}")]
    public async Task<ActionResult<SharedAnalysisDetailDto>> Shared(Guid historyId, CancellationToken cancellationToken)
    {
        var detail = await _sharing.GetSharedAsync(historyId, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    /// <summary>The stored answer of a shared version, exactly as the owner saw it; the source is never queried.</summary>
    [HttpGet("with-me/{historyId:guid}/versions/{versionNumber:int}/result")]
    public async Task<ActionResult<AskResponse>> SharedResult(Guid historyId, int versionNumber, CancellationToken cancellationToken)
    {
        var result = await _sharing.GetSharedVersionResultAsync(historyId, versionNumber, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("with-me/{historyId:guid}/versions/{versionNumber:int}/report")]
    public async Task<IActionResult> SharedReport(Guid historyId, int versionNumber, CancellationToken cancellationToken)
    {
        var download = await _sharing.OpenSharedVersionReportAsync(historyId, versionNumber, cancellationToken);
        if (download is null)
        {
            return NotFound();
        }

        Response.Headers.Append("X-Content-Type-Options", "nosniff");
        return File(download.Content, download.ContentType, download.FileName);
    }

    [HttpGet("/api/history/{historyId:guid}/shares")]
    public async Task<ActionResult<IReadOnlyList<ShareDto>>> SharesOf(Guid historyId, CancellationToken cancellationToken)
        => Ok(await _sharing.ListSharesOfAsync(historyId, cancellationToken));

    [HttpPost("/api/history/{historyId:guid}/shares")]
    public async Task<ActionResult<IReadOnlyList<ShareDto>>> Share(Guid historyId, [FromBody] CreateShareRequest request, CancellationToken cancellationToken)
        => Ok(await _sharing.ShareAsync(historyId, new CreateShareCommand(request.RecipientUserIds, request.Message, request.ExpiresAt), cancellationToken));

    [HttpDelete("/api/history/{historyId:guid}/shares/{shareId:guid}")]
    public async Task<IActionResult> Revoke(Guid historyId, Guid shareId, CancellationToken cancellationToken)
    {
        await _sharing.RevokeAsync(historyId, shareId, cancellationToken);
        return NoContent();
    }

    /// <summary>Emails a version (owner's text, optional PDF) under the classification rules; 400 with a stable code when blocked.</summary>
    [HttpPost("/api/history/{historyId:guid}/email")]
    public async Task<ActionResult<ShareEmailResult>> Email(Guid historyId, [FromBody] SendShareEmailRequest request, CancellationToken cancellationToken)
        => Ok(await _sharing.SendEmailAsync(historyId, new SendShareEmailCommand(request.VersionNumber, request.Recipients, request.Subject, request.Body, request.AttachReport, request.DisclosureApproved), cancellationToken));
}
