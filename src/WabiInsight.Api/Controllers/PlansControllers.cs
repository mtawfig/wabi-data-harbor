using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Authorization;
using WabiInsight.Core.Plans;

namespace WabiInsight.Api.Controllers;

public sealed record SaveServicePlanRequest(
    [Required, MaxLength(32)] string PlanCode,
    [Required, MaxLength(100)] string NameEn,
    [Required, MaxLength(100)] string NameAr,
    [Range(0.01, 1_000_000)] decimal? MonthlyAiSpendLimit,
    bool IsActive = true,
    [Range(0, 1000)] int SortOrder = 100);

public sealed record AssignServicePlanRequest(Guid? PlanId);

/// <summary>Service plans and their monthly AI spending limits; which plan an organization has (P13.46).</summary>
[ApiController]
[Route("api/platform/plans")]
[Authorize(Policy = Policies.PlatformAdministrator)]
public sealed class PlatformPlansController : ControllerBase
{
    private readonly IServicePlanService _plans;

    public PlatformPlansController(IServicePlanService plans)
    {
        _plans = plans;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ServicePlanDto>>> List(CancellationToken cancellationToken) => Ok(await _plans.ListAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<ServicePlanDto>> Create([FromBody] SaveServicePlanRequest request, CancellationToken cancellationToken)
    {
        var created = await _plans.CreateAsync(ToCommand(request), cancellationToken);
        return Created($"/api/platform/plans/{created.PlanId}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ServicePlanDto>> Update(Guid id, [FromBody] SaveServicePlanRequest request, CancellationToken cancellationToken)
        => Ok(await _plans.UpdateAsync(id, ToCommand(request), cancellationToken));

    /// <summary>The organization's plan, its limit and the month's AI spending so far (amounts only).</summary>
    [HttpGet("organizations/{organizationId:guid}")]
    public async Task<ActionResult<OrganizationPlanDto>> OrganizationPlan(Guid organizationId, CancellationToken cancellationToken)
        => Ok(await _plans.GetOrganizationPlanAsync(organizationId, cancellationToken));

    [HttpPut("organizations/{organizationId:guid}")]
    public async Task<ActionResult<OrganizationPlanDto>> Assign(Guid organizationId, [FromBody] AssignServicePlanRequest request, CancellationToken cancellationToken)
        => Ok(await _plans.AssignAsync(organizationId, request.PlanId, cancellationToken));

    private static SaveServicePlanCommand ToCommand(SaveServicePlanRequest r) => new(r.PlanCode, r.NameEn, r.NameAr, r.MonthlyAiSpendLimit, r.IsActive, r.SortOrder);
}

/// <summary>The signed-in user's organization against its plan's AI spending limit: the state for everyone, the figures for its administrators.</summary>
[ApiController]
[Route("api/ai/spending")]
[Authorize(Policy = Policies.OrganizationUser)]
public sealed class AiSpendingController : ControllerBase
{
    private readonly IAiSpendingGuard _spending;

    public AiSpendingController(IAiSpendingGuard spending)
    {
        _spending = spending;
    }

    [HttpGet]
    public async Task<ActionResult<AiSpendingDto>> Get(CancellationToken cancellationToken) => Ok(await _spending.GetCurrentAsync(cancellationToken));
}
