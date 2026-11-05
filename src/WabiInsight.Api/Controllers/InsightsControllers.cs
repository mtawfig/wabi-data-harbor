using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Authorization;
using WabiInsight.Api.Contracts;
using WabiInsight.Core.Insights;

namespace WabiInsight.Api.Controllers;

/// <summary>Platform Insights Configuration (specification §12): the global catalog, maintained by Platform Administrators.</summary>
[ApiController]
[Route("api/platform/insights-catalog")]
[Authorize(Policy = Policies.PlatformAdministrator)]
public sealed class PlatformInsightsController : ControllerBase
{
    private readonly IInsightsCatalogService _catalog;

    public PlatformInsightsController(IInsightsCatalogService catalog)
    {
        _catalog = catalog;
    }

    [HttpGet]
    public async Task<ActionResult<InsightsCatalogDto>> Get(CancellationToken cancellationToken) => Ok(await _catalog.GetCatalogAsync(cancellationToken));

    [HttpGet("source")]
    public async Task<ActionResult<InsightsSourceStatus>> Source(CancellationToken cancellationToken) => Ok(await _catalog.CheckSourceAsync(cancellationToken));

    [HttpPost("sync")]
    public async Task<ActionResult<InsightsSyncResult>> Sync(CancellationToken cancellationToken) => Ok(await _catalog.SyncAsync(cancellationToken));

    [HttpPut("objects/{id:guid}")]
    public async Task<ActionResult<InsightsObjectDto>> UpdateObject(Guid id, [FromBody] UpdateInsightsObjectRequest request, CancellationToken cancellationToken)
        => Ok(await _catalog.UpdateObjectAsync(id, new UpdateInsightsObjectCommand(request.DisplayNameEn, request.DisplayNameAr, request.Description, request.IsApproved, request.SortOrder), cancellationToken));

    [HttpPut("columns/{id:guid}")]
    public async Task<ActionResult<InsightsColumnDto>> UpdateColumn(Guid id, [FromBody] UpdateInsightsColumnRequest request, CancellationToken cancellationToken)
        => Ok(await _catalog.UpdateColumnAsync(id, new UpdateInsightsColumnCommand(request.DisplayNameEn, request.DisplayNameAr, request.Description, request.IsApproved), cancellationToken));

    [HttpPost("metrics")]
    public async Task<ActionResult<InsightsMetricDto>> CreateMetric([FromBody] SaveInsightsMetricRequest request, CancellationToken cancellationToken)
    {
        var created = await _catalog.CreateMetricAsync(request.ToCommand(), cancellationToken);
        return Created($"/api/platform/insights-catalog/metrics/{created.MetricId}", created);
    }

    [HttpPut("metrics/{id:guid}")]
    public async Task<ActionResult<InsightsMetricDto>> UpdateMetric(Guid id, [FromBody] SaveInsightsMetricRequest request, CancellationToken cancellationToken)
        => Ok(await _catalog.UpdateMetricAsync(id, request.ToCommand(), cancellationToken));

    [HttpDelete("metrics/{id:guid}")]
    public async Task<IActionResult> DeleteMetric(Guid id, CancellationToken cancellationToken)
    {
        await _catalog.DeleteMetricAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>The SQL a metric compiles to, with the tenant placeholder. Never executed here.</summary>
    [HttpPost("metrics/{id:guid}/sql")]
    public async Task<ActionResult<SqlPreviewResponse>> PreviewSql(Guid id, [FromBody] MetricQueryRequestBody request, CancellationToken cancellationToken)
    {
        var built = await _catalog.PreviewSqlAsync(id, request.ToRequest(), cancellationToken);
        return Ok(new SqlPreviewResponse(built.Sql, built.Parameters.ToDictionary(p => p.Key, p => p.Key == InsightsSqlBuilder.TenantParameter ? "<organization of the signed-in user>" : Convert.ToString(p.Value, CultureInfo.InvariantCulture) ?? string.Empty)));
    }
}

/// <summary>Organization side of Platform Insights: the approved catalog and metric queries, always scoped to the caller's organization.</summary>
[ApiController]
[Route("api/insights")]
[Authorize(Policy = Policies.OrganizationAdmin)]
public sealed class InsightsController : ControllerBase
{
    private readonly IInsightsQueryService _queries;

    public InsightsController(IInsightsQueryService queries)
    {
        _queries = queries;
    }

    [HttpGet("catalog")]
    public async Task<ActionResult<OrganizationInsightsCatalog>> Catalog(CancellationToken cancellationToken) => Ok(await _queries.GetApprovedCatalogAsync(cancellationToken));

    [HttpPost("metrics/{code}/query")]
    public async Task<ActionResult<MetricResult>> Query(string code, [FromBody] MetricQueryRequestBody request, CancellationToken cancellationToken)
        => Ok(await _queries.QueryMetricAsync(code, request.ToRequest(), cancellationToken));
}
