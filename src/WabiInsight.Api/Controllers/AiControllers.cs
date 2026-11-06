using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Authorization;
using WabiInsight.Api.Security;
using WabiInsight.Api.Contracts;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Entities.Ai;

namespace WabiInsight.Api.Controllers;

/// <summary>Platform AI providers (specification §18): approved endpoints, the platform default and the allow-list of types organizations may configure.</summary>
[ApiController]
[Route("api/platform/ai-providers")]
[Authorize(Policy = Policies.PlatformAdministrator)]
public sealed class PlatformAiProvidersController : ControllerBase
{
    private readonly IAiProviderService _providers;

    public PlatformAiProvidersController(IAiProviderService providers)
    {
        _providers = providers;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AiProviderDto>>> List(CancellationToken cancellationToken) => Ok(await _providers.ListPlatformAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<AiProviderDto>> Create([FromBody] SaveAiProviderRequest request, CancellationToken cancellationToken)
    {
        var created = await _providers.CreatePlatformAsync(request.ToCommand(), cancellationToken);
        return Created($"/api/platform/ai-providers/{created.ProviderId}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AiProviderDto>> Update(Guid id, [FromBody] SaveAiProviderRequest request, CancellationToken cancellationToken) => Ok(await _providers.UpdatePlatformAsync(id, request.ToCommand(), cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _providers.DeletePlatformAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/default")]
    public async Task<ActionResult<AiProviderDto>> SetDefault(Guid id, CancellationToken cancellationToken) => Ok(await _providers.SetDefaultAsync(id, cancellationToken));

    [HttpPost("{id:guid}/test")]
    public async Task<ActionResult<AiProviderTestResult>> Test(Guid id, CancellationToken cancellationToken) => Ok(await _providers.TestAsync(id, cancellationToken));

    [HttpGet("allowed-types")]
    public async Task<ActionResult<IReadOnlyList<AiProviderType>>> AllowedTypes(CancellationToken cancellationToken) => Ok(await _providers.GetAllowedTypesAsync(cancellationToken));

    [HttpPut("allowed-types")]
    public async Task<ActionResult<IReadOnlyList<AiProviderType>>> SetAllowedTypes([FromBody] SetAllowedProviderTypesRequest request, CancellationToken cancellationToken) => Ok(await _providers.SetAllowedTypesAsync(request.Types, cancellationToken));
}

/// <summary>Organization side: the providers it may use, its own configurations (allow-listed types) and its selection.</summary>
[ApiController]
[Route("api/ai-providers")]
[Authorize(Policy = Policies.OrganizationAdmin)]
public sealed class AiProvidersController : ControllerBase
{
    private readonly IAiProviderService _providers;

    public AiProvidersController(IAiProviderService providers)
    {
        _providers = providers;
    }

    [HttpGet]
    public async Task<ActionResult<OrganizationAiSettingsDto>> Get(CancellationToken cancellationToken) => Ok(await _providers.GetOrganizationSettingsAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<AiProviderDto>> Create([FromBody] SaveAiProviderRequest request, CancellationToken cancellationToken)
    {
        var created = await _providers.CreateOrganizationAsync(request.ToCommand(), cancellationToken);
        return Created($"/api/ai-providers/{created.ProviderId}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AiProviderDto>> Update(Guid id, [FromBody] SaveAiProviderRequest request, CancellationToken cancellationToken) => Ok(await _providers.UpdateOrganizationAsync(id, request.ToCommand(), cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _providers.DeleteOrganizationAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPut("selection")]
    public async Task<ActionResult<OrganizationAiSettingsDto>> Select([FromBody] SelectAiProviderRequest request, CancellationToken cancellationToken) => Ok(await _providers.SelectForOrganizationAsync(request.ProviderId, cancellationToken));

    [HttpPost("{id:guid}/test")]
    public async Task<ActionResult<AiProviderTestResult>> Test(Guid id, CancellationToken cancellationToken) => Ok(await _providers.TestAsync(id, cancellationToken));
}

/// <summary>Secure AI workflow (specification §19). P8.1 exposes the planning step; execution and interpretation arrive with P8.2.</summary>
[ApiController]
[Route("api/ai")]
[Authorize(Policy = Policies.OrganizationUser)]
public sealed class AiController : ControllerBase
{
    private readonly IQueryPlanner _planner;
    private readonly IAnalysisService _analysis;

    public AiController(IQueryPlanner planner, IAnalysisService analysis)
    {
        _planner = planner;
        _analysis = analysis;
    }

    /// <summary>Applications the signed-in user may ask about (approved, active, with at least one permitted object).</summary>
    [HttpGet("applications")]
    public async Task<ActionResult<IReadOnlyList<AskableApplication>>> Applications(CancellationToken cancellationToken) => Ok(await _analysis.ListApplicationsAsync(cancellationToken));

    /// <summary>The full secure workflow: plan, compile, execute read-only, calculate, interpret.</summary>
    [HttpPost("ask")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(RateLimitingSetup.AiPolicy)]
    [ProducesResponseType(typeof(AskResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PlanRejectedResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Ask([FromBody] AskQuestionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _analysis.AskAsync(new AskRequest(request.ApplicationId, request.Question, request.Language, request.Interpret), cancellationToken));
        }
        catch (PlanRejectedException ex)
        {
            return UnprocessableEntity(new PlanRejectedResponse(ex.Stage, ex.Errors));
        }
        catch (Infrastructure.Ai.AnalysisExecutionException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new AnalysisFailedResponse("execute", ex.Code));
        }
    }

    /// <summary>The PDF report of an analysis the signed-in user ran; 404 for anyone else or when the answer had no report.</summary>
    [HttpGet("analyses/{analysisId:guid}/report")]
    public async Task<IActionResult> Report(Guid analysisId, CancellationToken cancellationToken)
    {
        var download = await _analysis.OpenReportAsync(analysisId, cancellationToken);
        if (download is null)
        {
            return NotFound();
        }

        Response.Headers.Append("X-Content-Type-Options", "nosniff");
        return File(download.Content, download.ContentType, download.FileName);
    }

    /// <summary>What the signed-in user may ask about in an application: the exact snapshot the model receives.</summary>
    [HttpGet("applications/{applicationId:guid}/snapshot")]
    public async Task<ActionResult<MetadataSnapshot>> Snapshot(Guid applicationId, CancellationToken cancellationToken) => Ok(await _planner.GetSnapshotAsync(applicationId, cancellationToken));

    [HttpPost("plan")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(RateLimitingSetup.AiPolicy)]
    [ProducesResponseType(typeof(PlanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PlanRejectedResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Plan([FromBody] PlanQuestionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _planner.PlanAsync(new PlanRequest(request.ApplicationId, request.Question, request.Language), cancellationToken));
        }
        catch (PlanRejectedException ex)
        {
            return UnprocessableEntity(new PlanRejectedResponse(ex.Stage, ex.Errors));
        }
    }
}
