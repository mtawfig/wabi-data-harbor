using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Authorization;
using WabiInsight.Core.Common;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Platform;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Api.Controllers;

/// <summary>Aggregated platform metrics for the operations dashboard (specification §13). Platform Administrators only.</summary>
[ApiController]
[Route("api/platform/dashboard")]
[Authorize(Policy = Policies.PlatformAdministrator)]
public sealed class PlatformDashboardController : ControllerBase
{
    private readonly IPlatformMetricsService _metrics;
    private readonly IClock _clock;

    public PlatformDashboardController(IPlatformMetricsService metrics, IClock clock)
    {
        _metrics = metrics;
        _clock = clock;
    }

    /// <summary>Date range presets map to from/to on the client; the API only takes explicit UTC bounds (default: last 30 days).</summary>
    [HttpGet]
    [ProducesResponseType<PlatformDashboard>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlatformDashboard>> Get(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] MetricBucket bucket = MetricBucket.Day,
        [FromQuery] Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var period = new MetricPeriod(from ?? now.AddDays(-30).Date, to ?? now);
        if (period.To < period.From || (period.To - period.From) > TimeSpan.FromDays(400))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid period", detail: "The period must be positive and at most 400 days.", type: "https://wabi-insight.dev/problems/validation");
        }

        return Ok(await _metrics.GetDashboardAsync(period, bucket, organizationId, cancellationToken));
    }
}

/// <summary>On-demand health report for the System Health page. The anonymous /health endpoints stay minimal; this one carries details.</summary>
[ApiController]
[Route("api/platform/health")]
[Authorize(Policy = Policies.PlatformAdministrator)]
public sealed class PlatformHealthController : ControllerBase
{
    private readonly IPlatformHealthService _health;

    public PlatformHealthController(IPlatformHealthService health)
    {
        _health = health;
    }

    [HttpGet]
    [ProducesResponseType<PlatformHealthReport>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlatformHealthReport>> Get(CancellationToken cancellationToken) => Ok(await _health.CheckAsync(cancellationToken));
}

/// <summary>Audit trail for Platform Administrators (specification §29). Read-only; rows are append-only in the database.</summary>
[ApiController]
[Route("api/platform/audit")]
[Authorize(Policy = Policies.PlatformAdministrator)]
public sealed class PlatformAuditController : ControllerBase
{
    private readonly IAuditQueryService _audit;

    public PlatformAuditController(IAuditQueryService audit)
    {
        _audit = audit;
    }

    [HttpGet]
    [ProducesResponseType<PagedResult<AuditLogRow>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AuditLogRow>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? eventType = null,
        [FromQuery] Guid? organizationId = null,
        [FromQuery] PrincipalType? actorType = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
        => Ok(await _audit.ListAsync(new AuditLogQuery(page, pageSize, from, to, eventType, organizationId, actorType, search), cancellationToken));

    [HttpGet("event-types")]
    public async Task<ActionResult<IReadOnlyList<string>>> EventTypes(CancellationToken cancellationToken) => Ok(await _audit.ListEventTypesAsync(cancellationToken));
}
