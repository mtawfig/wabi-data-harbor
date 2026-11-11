using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Contracts;
using WabiInsight.Api.Authorization;
using WabiInsight.Api.Security;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Analysis;
using WabiInsight.Core.Entities.Analysis;
using WabiInsight.Core.Organizations;
using WabiInsight.Infrastructure.Ai;

namespace WabiInsight.Api.Controllers;

/// <summary>Persistent analysis history of the signed-in organization user (specification §23): owner-scoped, never re-queries the source on reopen.</summary>
[ApiController]
[Route("api/history")]
[Authorize(Policy = Policies.OrganizationUser)]
public sealed class HistoryController : ControllerBase
{
    private readonly IAnalysisHistoryService _history;

    public HistoryController(IAnalysisHistoryService history)
    {
        _history = history;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<HistorySummaryDto>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] string? search = null, [FromQuery] Guid? applicationId = null, [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null, [FromQuery] bool? favorite = null, [FromQuery] bool archived = false, [FromQuery] Guid? collectionId = null, [FromQuery] AnalysisType? type = null, CancellationToken cancellationToken = default)
        => Ok(await _history.ListAsync(new HistoryListQuery(page, pageSize, search, applicationId, from, to, favorite, archived, collectionId, type), cancellationToken));

    /// <summary>The caller's generated files: study PDFs and exports, newest first.</summary>
    [HttpGet("reports")]
    public async Task<ActionResult<PagedResult<ReportFileDto>>> Reports([FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] string? search = null, [FromQuery] ExportFormat? format = null, [FromQuery] Guid? historyId = null, CancellationToken cancellationToken = default)
        => Ok(await _history.ListReportsAsync(new ReportListQuery(page, pageSize, search, format, historyId), cancellationToken));

    [HttpGet("artifacts/{artifactId:guid}")]
    public async Task<IActionResult> Artifact(Guid artifactId, CancellationToken cancellationToken)
    {
        var download = await _history.OpenArtifactAsync(artifactId, cancellationToken);
        if (download is null)
        {
            return NotFound();
        }

        Response.Headers.Append("X-Content-Type-Options", "nosniff");
        return File(download.Content, download.ContentType, download.FileName);
    }

    /// <summary>Renders a version to PDF, DOCX or XLSX, stores it in the reports library and returns the file.</summary>
    [HttpPost("{historyId:guid}/versions/{versionNumber:int}/export/{format}")]
    public async Task<IActionResult> Export(Guid historyId, int versionNumber, ExportFormat format, CancellationToken cancellationToken)
    {
        var download = await _history.ExportAsync(historyId, versionNumber, format, cancellationToken);
        Response.Headers.Append("X-Content-Type-Options", "nosniff");
        return File(download.Content, download.ContentType, download.FileName);
    }

    [HttpGet("collections")]
    public async Task<ActionResult<IReadOnlyList<CollectionSummaryDto>>> Collections(CancellationToken cancellationToken) => Ok(await _history.ListCollectionsAsync(cancellationToken));

    [HttpPost("collections")]
    public async Task<ActionResult<CollectionSummaryDto>> CreateCollection([FromBody] SaveCollectionRequest request, CancellationToken cancellationToken)
    {
        var created = await _history.CreateCollectionAsync(new SaveCollectionCommand(request.Name, request.Description), cancellationToken);
        return Created($"/api/history/collections/{created.CollectionId}", created);
    }

    [HttpPut("collections/{collectionId:guid}")]
    public async Task<ActionResult<CollectionSummaryDto>> UpdateCollection(Guid collectionId, [FromBody] SaveCollectionRequest request, CancellationToken cancellationToken)
        => Ok(await _history.UpdateCollectionAsync(collectionId, new SaveCollectionCommand(request.Name, request.Description), cancellationToken));

    [HttpDelete("collections/{collectionId:guid}")]
    public async Task<IActionResult> DeleteCollection(Guid collectionId, CancellationToken cancellationToken)
    {
        await _history.DeleteCollectionAsync(collectionId, cancellationToken);
        return NoContent();
    }

    [HttpPost("collections/{collectionId:guid}/items")]
    public async Task<IActionResult> AddToCollection(Guid collectionId, [FromBody] CollectionItemRequest request, CancellationToken cancellationToken)
    {
        await _history.AddToCollectionAsync(collectionId, request.HistoryId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("collections/{collectionId:guid}/items/{historyId:guid}")]
    public async Task<IActionResult> RemoveFromCollection(Guid collectionId, Guid historyId, CancellationToken cancellationToken)
    {
        await _history.RemoveFromCollectionAsync(collectionId, historyId, cancellationToken);
        return NoContent();
    }

    [HttpGet("{historyId:guid}")]
    public async Task<ActionResult<HistoryDetailDto>> Get(Guid historyId, CancellationToken cancellationToken)
    {
        var detail = await _history.GetAsync(historyId, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    /// <summary>The stored answer of a version, exactly as it was shown; the source is never queried again.</summary>
    [HttpGet("{historyId:guid}/versions/{versionNumber:int}/result")]
    public async Task<ActionResult<AskResponse>> VersionResult(Guid historyId, int versionNumber, CancellationToken cancellationToken)
    {
        var result = await _history.GetVersionResultAsync(historyId, versionNumber, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{historyId:guid}/versions/{versionNumber:int}/report")]
    public async Task<IActionResult> VersionReport(Guid historyId, int versionNumber, CancellationToken cancellationToken)
    {
        var download = await _history.OpenVersionReportAsync(historyId, versionNumber, cancellationToken);
        if (download is null)
        {
            return NotFound();
        }

        Response.Headers.Append("X-Content-Type-Options", "nosniff");
        return File(download.Content, download.ContentType, download.FileName);
    }

    [HttpPut("{historyId:guid}")]
    public async Task<ActionResult<HistorySummaryDto>> Rename(Guid historyId, [FromBody] RenameHistoryRequest request, CancellationToken cancellationToken)
        => Ok(await _history.RenameAsync(historyId, request.Title, cancellationToken));

    [HttpPost("{historyId:guid}/favorite")]
    public async Task<ActionResult<HistorySummaryDto>> Favorite(Guid historyId, [FromBody] SetFavoriteRequest request, CancellationToken cancellationToken)
        => Ok(await _history.SetFavoriteAsync(historyId, request.IsFavorite, cancellationToken));

    [HttpPost("{historyId:guid}/archive")]
    public async Task<ActionResult<HistorySummaryDto>> Archive(Guid historyId, [FromBody] SetArchivedRequest request, CancellationToken cancellationToken)
        => Ok(await _history.SetArchivedAsync(historyId, request.IsArchived, cancellationToken));

    [HttpDelete("{historyId:guid}")]
    public async Task<IActionResult> Delete(Guid historyId, CancellationToken cancellationToken)
    {
        await _history.DeleteAsync(historyId, cancellationToken);
        return NoContent();
    }

    /// <summary>Runs the saved prompt again under the caller's current permissions; the outcome is a new version (422 rejected, 502 execution failure).</summary>
    [HttpPost("{historyId:guid}/rerun")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(RateLimitingSetup.AiPolicy)]
    [ProducesResponseType(typeof(AskResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PlanRejectedResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Rerun(Guid historyId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _history.RerunAsync(historyId, cancellationToken));
        }
        catch (PlanRejectedException ex)
        {
            return UnprocessableEntity(new PlanRejectedResponse(ex.Stage, ex.Errors));
        }
        catch (AnalysisExecutionException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new AnalysisFailedResponse("execute", ex.Code));
        }
    }
}
