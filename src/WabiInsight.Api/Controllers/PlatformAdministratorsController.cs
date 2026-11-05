using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Authorization;
using WabiInsight.Api.Contracts;
using WabiInsight.Core.Authentication;

namespace WabiInsight.Api.Controllers;

/// <summary>Platform Administrator management. P1.3 ships only the administrator-assisted password reset; P3.2 adds the rest.</summary>
[ApiController]
[Route("api/platform/administrators")]
[Authorize(Policy = Policies.PlatformAdministrator)]
public sealed class PlatformAdministratorsController : ControllerBase
{
    private readonly IAdministratorPasswordService _passwords;

    public PlatformAdministratorsController(IAdministratorPasswordService passwords)
    {
        _passwords = passwords;
    }

    /// <summary>Issues a one-time temporary password for another administrator; it is returned once and never stored in plain text.</summary>
    [HttpPost("{id:guid}/reset-password")]
    [ProducesResponseType<TemporaryPasswordResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TemporaryPasswordResponse>> ResetPassword(Guid id, CancellationToken cancellationToken)
    {
        var temporary = await _passwords.ResetPasswordAsync(id, cancellationToken);
        return temporary is null ? NotFound() : Ok(new TemporaryPasswordResponse(temporary, true));
    }
}
