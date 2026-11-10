using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Authentication;
using WabiInsight.Api.Authorization;
using WabiInsight.Api.Contracts;
using WabiInsight.Core.Entities.Support;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.SupportAccess;

namespace WabiInsight.Api.Controllers;

/// <summary>Platform side of support access (specification §16): propose, enter/exit support mode, execute permitted operations, complete, revoke.</summary>
[ApiController]
[Route("api/platform/support")]
[Authorize(Policy = Policies.PlatformAdministrator)]
public sealed class PlatformSupportAccessController : ControllerBase
{
    private readonly ISupportAccessService _grants;
    private readonly ISupportOperationExecutor _executor;

    public PlatformSupportAccessController(ISupportAccessService grants, ISupportOperationExecutor executor)
    {
        _grants = grants;
        _executor = executor;
    }

    [HttpGet("operations")]
    public ActionResult<IReadOnlyList<SupportOperationDefinition>> Catalog() => Ok(_grants.Catalog);

    [HttpPost("tickets/{ticketId:guid}/access-grants")]
    public async Task<ActionResult<SupportAccessGrantDto>> Propose(Guid ticketId, [FromBody] ProposeGrantRequest request, CancellationToken cancellationToken)
    {
        var grant = await _grants.ProposeAsync(ticketId, new ProposeGrantCommand(request.Operations, request.Reason, request.ExpiryHours), cancellationToken);
        return Created($"/api/platform/support/access-grants/{grant.GrantId}", grant);
    }

    [HttpGet("tickets/{ticketId:guid}/access-grants")]
    public async Task<ActionResult<IReadOnlyList<SupportAccessGrantDto>>> ForTicket(Guid ticketId, CancellationToken cancellationToken)
        => Ok(await _grants.ListForTicketAsync(ticketId, cancellationToken));

    [HttpGet("tickets/{ticketId:guid}/targets")]
    public async Task<ActionResult<SupportTargetsDto>> Targets(Guid ticketId, CancellationToken cancellationToken)
        => Ok(await _grants.ListTargetsAsync(ticketId, cancellationToken));

    /// <summary>The organization's user with exactly this address (the ticket names them); the user list itself is never returned.</summary>
    [HttpGet("tickets/{ticketId:guid}/targets/user")]
    public async Task<ActionResult<SupportTargetUser>> TargetUser(Guid ticketId, [FromQuery] string? email, CancellationToken cancellationToken)
        => Ok(await _grants.FindTargetUserAsync(ticketId, email ?? string.Empty, cancellationToken));

    /// <summary>The organization's role with exactly this code, for a role the organization defined itself.</summary>
    [HttpGet("tickets/{ticketId:guid}/targets/role")]
    public async Task<ActionResult<SupportTargetRole>> TargetRole(Guid ticketId, [FromQuery] string? code, CancellationToken cancellationToken)
        => Ok(await _grants.FindTargetRoleAsync(ticketId, code ?? string.Empty, cancellationToken));

    [HttpGet("access-grants")]
    public async Task<ActionResult<PagedResult<SupportAccessGrantDto>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] SupportAccessGrantStatus? status = null, [FromQuery] Guid? organizationId = null, [FromQuery] bool? pending = null, CancellationToken cancellationToken = default)
        => Ok(await _grants.ListAsync(new GrantListQuery(page, pageSize, status, organizationId, null, pending), cancellationToken));

    [HttpGet("access-grants/{id:guid}")]
    public async Task<ActionResult<SupportAccessGrantDto>> Get(Guid id, CancellationToken cancellationToken) => Ok(await _grants.GetAsync(id, cancellationToken));

    [HttpPost("access-grants/{id:guid}/enter")]
    public async Task<ActionResult<SupportSessionDto>> Enter(Guid id, CancellationToken cancellationToken) => Ok(await _grants.EnterAsync(id, cancellationToken));

    [HttpPost("access-grants/{id:guid}/complete")]
    public async Task<ActionResult<SupportAccessGrantDto>> Complete(Guid id, [FromBody] GrantDecisionRequest request, CancellationToken cancellationToken)
        => Ok(await _grants.CompleteAsync(id, new GrantDecisionCommand(request.Note), cancellationToken));

    [HttpPost("access-grants/{id:guid}/revoke")]
    public async Task<ActionResult<SupportAccessGrantDto>> Revoke(Guid id, [FromBody] GrantDecisionRequest request, CancellationToken cancellationToken)
        => Ok(await _grants.RevokeAsync(id, new GrantDecisionCommand(request.Note), cancellationToken));

    [HttpGet("session")]
    [BackgroundPoll]
    public async Task<ActionResult<SupportSessionDto?>> Session(CancellationToken cancellationToken)
    {
        var session = await _grants.CurrentSessionAsync(cancellationToken);
        return session is null ? NoContent() : Ok(session);
    }

    [HttpPost("session/exit")]
    public async Task<IActionResult> Exit(CancellationToken cancellationToken)
    {
        await _grants.ExitAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Runs one permitted operation inside the active session's organization. Scope and expiry are enforced per call.</summary>
    [HttpPost("session/execute")]
    [Authorize(Policy = Policies.SupportMode)]
    public async Task<ActionResult<SupportOperationResult>> Execute([FromBody] ExecuteOperationRequest request, CancellationToken cancellationToken)
        => Ok(await _executor.ExecuteAsync(new ExecuteOperationCommand(request.Operation, request.TargetReference, request.Payload), cancellationToken));
}

/// <summary>Organization side: see grants on visible tickets; Organization Administrators approve, decline and revoke.</summary>
[ApiController]
[Route("api/support")]
[Authorize(Policy = Policies.OrganizationUser)]
public sealed class SupportAccessController : ControllerBase
{
    private readonly ISupportAccessService _grants;

    public SupportAccessController(ISupportAccessService grants)
    {
        _grants = grants;
    }

    [HttpGet("tickets/{ticketId:guid}/access-grants")]
    public async Task<ActionResult<IReadOnlyList<SupportAccessGrantDto>>> ForTicket(Guid ticketId, CancellationToken cancellationToken)
        => Ok(await _grants.ListForTicketAsync(ticketId, cancellationToken));

    [HttpGet("access-grants")]
    public async Task<ActionResult<PagedResult<SupportAccessGrantDto>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] SupportAccessGrantStatus? status = null, [FromQuery] bool? pending = null, CancellationToken cancellationToken = default)
        => Ok(await _grants.ListAsync(new GrantListQuery(page, pageSize, status, null, null, pending), cancellationToken));

    [HttpGet("access-grants/{id:guid}")]
    public async Task<ActionResult<SupportAccessGrantDto>> Get(Guid id, CancellationToken cancellationToken) => Ok(await _grants.GetAsync(id, cancellationToken));

    [HttpPost("access-grants/{id:guid}/approve")]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    public async Task<ActionResult<SupportAccessGrantDto>> Approve(Guid id, [FromBody] GrantDecisionRequest request, CancellationToken cancellationToken)
        => Ok(await _grants.ApproveAsync(id, new GrantDecisionCommand(request.Note, request.ExpiryHours), cancellationToken));

    [HttpPost("access-grants/{id:guid}/decline")]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    public async Task<ActionResult<SupportAccessGrantDto>> Decline(Guid id, [FromBody] GrantDecisionRequest request, CancellationToken cancellationToken)
        => Ok(await _grants.DeclineAsync(id, new GrantDecisionCommand(request.Note), cancellationToken));

    [HttpPost("access-grants/{id:guid}/revoke")]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    public async Task<ActionResult<SupportAccessGrantDto>> Revoke(Guid id, [FromBody] GrantDecisionRequest request, CancellationToken cancellationToken)
        => Ok(await _grants.RevokeAsync(id, new GrantDecisionCommand(request.Note), cancellationToken));
}
