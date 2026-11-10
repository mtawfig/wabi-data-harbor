using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WabiInsight.Api.Contracts;
using WabiInsight.Api.Security;
using WabiInsight.Core.Bootstrap;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Api.Controllers;

/// <summary>
/// Protected first-administrator initialization (specification §7). Status is public but tells an anonymous
/// caller only what the set-up page needs (completed, enabled, migrations pending); the administrator's e-mail,
/// the date and the schema version go to signed-in Platform Administrators only (P13.25). Initialization requires
/// the deployment secret and works once.
/// </summary>
[ApiController]
[Route("api/bootstrap")]
[AllowAnonymous]
public sealed class BootstrapController : ControllerBase
{
    private readonly IBootstrapService _bootstrap;
    private readonly ITenantContext _tenantContext;

    public BootstrapController(IBootstrapService bootstrap, ITenantContext tenantContext)
    {
        _bootstrap = bootstrap;
        _tenantContext = tenantContext;
    }

    [HttpGet("status")]
    [ProducesResponseType<BootstrapStatusResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BootstrapStatusResponse>> Status(CancellationToken cancellationToken)
    {
        var s = await _bootstrap.GetStatusAsync(cancellationToken);
        // Anonymous by design (the set-up page runs before anyone can sign in), so the first administrator's address
        // is not handed to whoever asks: it is half of a sign-in and the target of lockout attempts.
        var detailed = _tenantContext.IsPlatformAdministrator;
        return Ok(new BootstrapStatusResponse(
            s.BootstrapCompleted,
            detailed ? s.BootstrapCompletedAt : null,
            detailed ? s.BootstrapAdministratorEmail : null,
            detailed ? s.SchemaVersion : null,
            s.PendingMigrations,
            s.BootstrapEnabled));
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
