using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WabiInsight.Api.Contracts;
using WabiInsight.Api.Security;
using WabiInsight.Core.Bootstrap;

namespace WabiInsight.Api.Controllers;

/// <summary>
/// Protected first-administrator initialization (specification §7). Status is public but reveals only
/// whether bootstrap completed; initialization requires the deployment secret and works once.
/// </summary>
[ApiController]
[Route("api/bootstrap")]
[AllowAnonymous]
public sealed class BootstrapController : ControllerBase
{
    private readonly IBootstrapService _bootstrap;

    public BootstrapController(IBootstrapService bootstrap)
    {
        _bootstrap = bootstrap;
    }

    [HttpGet("status")]
    [ProducesResponseType<BootstrapStatusResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BootstrapStatusResponse>> Status(CancellationToken cancellationToken)
    {
        var s = await _bootstrap.GetStatusAsync(cancellationToken);
        return Ok(new BootstrapStatusResponse(s.BootstrapCompleted, s.BootstrapCompletedAt, s.BootstrapAdministratorEmail, s.SchemaVersion, s.PendingMigrations, s.BootstrapEnabled));
    }

    [HttpPost("initialize")]
    [EnableRateLimiting(RateLimitingSetup.BootstrapPolicy)]
    [ProducesResponseType<BootstrapInitializeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BootstrapInitializeResponse>> Initialize([FromBody] BootstrapInitializeRequest request, CancellationToken cancellationToken)
    {
        var result = await _bootstrap.InitializeAsync(request.InitializationSecret, cancellationToken);

        return result.Outcome switch
        {
            BootstrapOutcome.Completed => Ok(new BootstrapInitializeResponse(result.AdministratorId!.Value, (await _bootstrap.GetStatusAsync(cancellationToken)).BootstrapAdministratorEmail ?? string.Empty)),
            BootstrapOutcome.Unauthorized => Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Bootstrap not authorized", type: "https://wabi-insight.dev/problems/bootstrap-unauthorized"),
            BootstrapOutcome.AlreadyCompleted => Problem(statusCode: StatusCodes.Status409Conflict, title: "Bootstrap already completed", type: "https://wabi-insight.dev/problems/bootstrap-completed"),
            BootstrapOutcome.Disabled => Problem(statusCode: StatusCodes.Status403Forbidden, title: "Bootstrap is disabled", type: "https://wabi-insight.dev/problems/bootstrap-disabled"),
            BootstrapOutcome.SchemaNotCurrent => Problem(statusCode: StatusCodes.Status409Conflict, title: "Database schema is not current", detail: "Apply migrations before bootstrapping.", type: "https://wabi-insight.dev/problems/bootstrap-schema"),
            _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bootstrap configuration invalid", detail: string.Join(", ", result.Errors), type: "https://wabi-insight.dev/problems/bootstrap-configuration"),
        };
    }
}
