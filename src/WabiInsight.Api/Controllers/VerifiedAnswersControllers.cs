using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Authorization;
using WabiInsight.Core.Analysis;
using WabiInsight.Core.Organizations;

namespace WabiInsight.Api.Controllers;

public sealed record AnswerFeedbackRequest(string Verdict, string? Reason);

public sealed record ReviewVerifiedAnswerRequest(string Action, string? Note);

/// <summary>The asker's verdict on their own saved answer (P13.53): correct or not correct, with an optional reason.</summary>
[ApiController]
[Route("api/history/{historyId:guid}/versions/{versionNumber:int}/feedback")]
[Authorize(Policy = Policies.OrganizationUser)]
public sealed class AnswerFeedbackController : ControllerBase
{
    private readonly IVerifiedAnswerService _answers;

    public AnswerFeedbackController(IVerifiedAnswerService answers)
    {
        _answers = answers;
    }

    [HttpGet]
    public async Task<ActionResult<AnswerFeedbackState>> Get(Guid historyId, int versionNumber, CancellationToken cancellationToken)
        => Ok(await _answers.GetFeedbackAsync(historyId, versionNumber, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<AnswerFeedbackState>> Submit(Guid historyId, int versionNumber, [FromBody] AnswerFeedbackRequest request, CancellationToken cancellationToken)
        => Ok(await _answers.SubmitFeedbackAsync(historyId, versionNumber, new SubmitAnswerFeedbackCommand(request.Verdict, request.Reason), cancellationToken));
}

/// <summary>Review of the organization's verified answers (P13.53): Organization Administrators only.</summary>
[ApiController]
[Route("api/verified-answers")]
[Authorize(Policy = Policies.OrganizationAdmin)]
public sealed class VerifiedAnswersController : ControllerBase
{
    private readonly IVerifiedAnswerService _answers;

    public VerifiedAnswersController(IVerifiedAnswerService answers)
    {
        _answers = answers;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<VerifiedAnswerListItem>>> List([FromQuery] string? status = null, [FromQuery] Guid? applicationId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
        => Ok(await _answers.ListAsync(new VerifiedAnswerListQuery(status, applicationId, page, pageSize), cancellationToken));

    [HttpGet("counts")]
    public async Task<ActionResult<VerifiedAnswerCounts>> Counts(CancellationToken cancellationToken)
        => Ok(await _answers.CountsAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VerifiedAnswerDetail>> Get(Guid id, CancellationToken cancellationToken)
    {
        var detail = await _answers.GetAsync(id, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost("{id:guid}/review")]
    public async Task<ActionResult<VerifiedAnswerDetail>> Review(Guid id, [FromBody] ReviewVerifiedAnswerRequest request, CancellationToken cancellationToken)
        => Ok(await _answers.ReviewAsync(id, new ReviewVerifiedAnswerCommand(request.Action, request.Note), cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _answers.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
