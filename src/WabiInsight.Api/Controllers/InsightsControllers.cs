using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Authorization;
using WabiInsight.Api.Contracts;
using WabiInsight.Core.Insights;
using WabiInsight.Core.Platform;

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

/// <summary>
/// Organization side of Platform Insights: the metrics the signed-in user may run (all approved ones for
/// Organization Administrators, granted ones otherwise), metric queries, the organization dashboard and
/// per-role metric grants. Everything is scoped to the caller's organization by the services.
/// </summary>
[ApiController]
[Route("api/insights")]
[Authorize(Policy = Policies.OrganizationUser)]
public sealed class InsightsController : ControllerBase
{
    private readonly IInsightsQueryService _queries;
    private readonly IMetricGrantService _grants;

    public InsightsController(IInsightsQueryService queries, IMetricGrantService grants)
    {
        _queries = queries;
        _grants = grants;
    }

    [HttpGet("catalog")]
    public async Task<ActionResult<OrganizationInsightsCatalog>> Catalog(CancellationToken cancellationToken) => Ok(await _queries.GetApprovedCatalogAsync(cancellationToken));

    [HttpPost("metrics/{code}/query")]
    public async Task<ActionResult<MetricResult>> Query(string code, [FromBody] MetricQueryRequestBody request, CancellationToken cancellationToken)
        => Ok(await _queries.QueryMetricAsync(code, request.ToRequest(), cancellationToken));

    /// <summary>Organization Administrator dashboard (specification §14).</summary>
    [HttpGet("dashboard")]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    public async Task<ActionResult<OrganizationDashboard>> Dashboard([FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null, [FromQuery] MetricBucket bucket = MetricBucket.Day, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var period = new MetricPeriod(from ?? now.AddDays(-30).Date, to ?? now);
        return Ok(await _queries.GetDashboardAsync(period, bucket, cancellationToken));
    }

    [HttpGet("roles/{roleId:guid}/metrics")]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    public async Task<ActionResult<RoleMetricGrantsDto>> RoleMetrics(Guid roleId, CancellationToken cancellationToken) => Ok(await _grants.GetAsync(roleId, cancellationToken));

    [HttpPut("roles/{roleId:guid}/metrics")]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    public async Task<ActionResult<RoleMetricGrantsDto>> SetRoleMetrics(Guid roleId, [FromBody] SetRoleMetricsRequest request, CancellationToken cancellationToken)
        => Ok(await _grants.SetAsync(roleId, request.MetricIds ?? [], cancellationToken));
}
