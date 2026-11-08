using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Authorization;
using WabiInsight.Api.Security;
using WabiInsight.Core.Entities.Monitoring;
using WabiInsight.Core.Monitoring;

namespace WabiInsight.Api.Controllers;

/// <summary>
/// Monitoring Rules (specification §35.1). Any organization user reaches the endpoints; the service allows
/// Organization Administrators (every rule) and Management users (their own data rules) and refuses everyone else.
/// </summary>
[ApiController]
[Route("api/monitoring")]
[Authorize(Policy = Policies.OrganizationUser)]
public sealed class MonitoringRulesController : ControllerBase
{
    private readonly IMonitoringRuleService _rules;

    public MonitoringRulesController(IMonitoringRuleService rules)
    {
        _rules = rules;
    }

    [HttpGet("templates")]
    public async Task<ActionResult<IReadOnlyList<RuleTemplateDto>>> Templates([FromQuery] Guid? ownerUserId, CancellationToken cancellationToken)
        => Ok(await _rules.ListTemplatesAsync(ownerUserId, cancellationToken));

    [HttpGet("owners")]
    public async Task<ActionResult<IReadOnlyList<OwnerOptionDto>>> Owners(CancellationToken cancellationToken)
        => Ok(await _rules.ListOwnerOptionsAsync(cancellationToken));

    [HttpGet("rules")]
    public async Task<ActionResult<IReadOnlyList<MonitoringRuleDto>>> List(CancellationToken cancellationToken)
        => Ok(await _rules.ListAsync(cancellationToken));

    [HttpGet("rules/{id:guid}")]
    public async Task<ActionResult<MonitoringRuleDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await _rules.GetAsync(id, cancellationToken));

    [HttpPost("rules")]
    public async Task<ActionResult<MonitoringRuleDto>> Create([FromBody] SaveMonitoringRuleCommand command, CancellationToken cancellationToken)
    {
        var created = await _rules.CreateAsync(command, cancellationToken);
        return Created($"/api/monitoring/rules/{created.RuleId}", created);
    }

    [HttpPut("rules/{id:guid}")]
    public async Task<ActionResult<MonitoringRuleDto>> Update(Guid id, [FromBody] SaveMonitoringRuleCommand command, CancellationToken cancellationToken)
        => Ok(await _rules.UpdateAsync(id, command, cancellationToken));

    [HttpPost("rules/{id:guid}/pause")]
    public async Task<ActionResult<MonitoringRuleDto>> Pause(Guid id, CancellationToken cancellationToken)
        => Ok(await _rules.SetActiveAsync(id, false, cancellationToken));

    [HttpPost("rules/{id:guid}/resume")]
    public async Task<ActionResult<MonitoringRuleDto>> Resume(Guid id, CancellationToken cancellationToken)
        => Ok(await _rules.SetActiveAsync(id, true, cancellationToken));

    [HttpDelete("rules/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _rules.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("rules/{id:guid}/test")]
    public async Task<ActionResult<RuleTestResultDto>> Test(Guid id, CancellationToken cancellationToken)
        => Ok(await _rules.TestAsync(id, cancellationToken));

    [HttpPost("rules/test")]
    public async Task<ActionResult<RuleTestResultDto>> TestDraft([FromBody] SaveMonitoringRuleCommand command, CancellationToken cancellationToken)
        => Ok(await _rules.TestDraftAsync(command, cancellationToken));

    /// <summary>
    /// P14.4: turns a plain-language description (English or Arabic) into a draft data rule bound to the owner's
    /// permitted catalogue. Nothing is saved; the user reviews the bindings and saves through <c>POST rules</c>.
    /// </summary>
    [HttpPost("rules/draft")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(RateLimitingSetup.AiPolicy)]
    public async Task<ActionResult<RuleDraftDto>> Draft([FromBody] RuleDraftRequest request, CancellationToken cancellationToken)
        => Ok(await _rules.DraftAsync(request, cancellationToken));

    [HttpGet("settings")]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    public async Task<ActionResult<MonitoringSettingsDto>> Settings(CancellationToken cancellationToken)
        => Ok(await _rules.GetSettingsAsync(cancellationToken));

    [HttpPut("settings")]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    public async Task<ActionResult<MonitoringSettingsDto>> UpdateSettings([FromBody] UpdateMonitoringSettingsCommand command, CancellationToken cancellationToken)
        => Ok(await _rules.UpdateSettingsAsync(command, cancellationToken));
}

public sealed record AlertActionRequest(string? Note);

/// <summary>The alert centre (specification §35.2): every organization user sees the alerts of rules they own, wrote or receive; administrators see all.</summary>
[ApiController]
[Route("api/monitoring/alerts")]
[Authorize(Policy = Policies.OrganizationUser)]
public sealed class AlertsController : ControllerBase
{
    private readonly IAlertService _alerts;

    public AlertsController(IAlertService alerts)
    {
        _alerts = alerts;
    }

    [HttpGet]
    public async Task<ActionResult<AlertPageDto>> List([FromQuery] AlertStatus? status, [FromQuery] AlertSeverity? severity, [FromQuery] Guid? ruleId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
        => Ok(await _alerts.ListAsync(new AlertQuery(status, severity, ruleId, page, pageSize), cancellationToken));

    [HttpGet("digest-preview")]
    public async Task<ActionResult<DigestPreviewDto>> DigestPreview(CancellationToken cancellationToken)
        => Ok(await _alerts.PreviewDigestAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AlertDetailDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await _alerts.GetAsync(id, cancellationToken));

    [HttpPost("{id:guid}/acknowledge")]
    public async Task<ActionResult<AlertDetailDto>> Acknowledge(Guid id, [FromBody] AlertActionRequest? request, CancellationToken cancellationToken)
        => Ok(await _alerts.ChangeStatusAsync(id, AlertStatus.Acknowledged, request?.Note, cancellationToken));

    [HttpPost("{id:guid}/resolve")]
    public async Task<ActionResult<AlertDetailDto>> Resolve(Guid id, [FromBody] AlertActionRequest? request, CancellationToken cancellationToken)
        => Ok(await _alerts.ChangeStatusAsync(id, AlertStatus.Resolved, request?.Note, cancellationToken));

    [HttpPost("{id:guid}/dismiss")]
    public async Task<ActionResult<AlertDetailDto>> Dismiss(Guid id, [FromBody] AlertActionRequest? request, CancellationToken cancellationToken)
        => Ok(await _alerts.ChangeStatusAsync(id, AlertStatus.Dismissed, request?.Note, cancellationToken));

    [HttpPost("{id:guid}/reopen")]
    public async Task<ActionResult<AlertDetailDto>> Reopen(Guid id, [FromBody] AlertActionRequest? request, CancellationToken cancellationToken)
        => Ok(await _alerts.ChangeStatusAsync(id, AlertStatus.Open, request?.Note, cancellationToken));

    /// <summary>Owner decision D2: suspension is a human action by an Organization Administrator, confirmed in the UI and audited.</summary>
    [HttpPost("{id:guid}/suspend-user")]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    public async Task<ActionResult<AlertDetailDto>> SuspendUser(Guid id, [FromBody] AlertActionRequest? request, CancellationToken cancellationToken)
        => Ok(await _alerts.SuspendSubjectAsync(id, request?.Note, cancellationToken));
}

/// <summary>Monitoring counts per organization for Platform Administrators; no alert content.</summary>
[ApiController]
[Route("api/platform/monitoring")]
[Authorize(Policy = Policies.PlatformAdministrator)]
public sealed class PlatformMonitoringController : ControllerBase
{
    private readonly IPlatformMonitoringService _monitoring;

    public PlatformMonitoringController(IPlatformMonitoringService monitoring)
    {
        _monitoring = monitoring;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<IReadOnlyList<OrganizationMonitoringCounts>>> Summary(CancellationToken cancellationToken)
        => Ok(await _monitoring.SummaryAsync(cancellationToken));
}
