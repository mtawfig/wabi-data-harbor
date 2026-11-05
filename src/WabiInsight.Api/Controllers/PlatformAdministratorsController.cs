using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Authorization;
using WabiInsight.Api.Contracts;
using WabiInsight.Core.Authentication;

namespace WabiInsight.Api.Controllers;

/// <summary>
/// Platform Administrator accounts. Temporary passwords are emailed to the new or reset administrator and
/// never returned to the caller (owner decision); the first sign-in forces a password change.
/// P3.2 adds listing, editing and deactivation.
/// </summary>
[ApiController]
[Route("api/platform/administrators")]
[Authorize(Policy = Policies.PlatformAdministrator)]
public sealed class PlatformAdministratorsController : ControllerBase
{
    private readonly IAdministratorAccountService _accounts;

    public PlatformAdministratorsController(IAdministratorAccountService accounts)
    {
        _accounts = accounts;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdministratorDto>>> List(CancellationToken cancellationToken) => Ok(await _accounts.ListAsync(cancellationToken));

    [HttpPost("{id:guid}/status")]
    public async Task<ActionResult<AccountProvisioningResponse>> SetStatus(Guid id, [FromBody] SetAdministratorStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _accounts.SetActiveAsync(id, request.IsActive, cancellationToken);
        return result.Outcome switch
        {
            AdministratorAccountOutcome.Success => Ok(new AccountProvisioningResponse(result.AdministratorId!.Value, result.DeliveredTo!, false, null)),
            AdministratorAccountOutcome.SelfResetNotAllowed => Problem(statusCode: StatusCodes.Status409Conflict, title: "You cannot change your own status", detail: "cannot_change_own_status", type: "https://wabi-insight.dev/problems/invalid-transition"),
            AdministratorAccountOutcome.LastActiveAdministrator => Problem(statusCode: StatusCodes.Status409Conflict, title: "At least one active platform administrator must remain", detail: "last_admin_protected", type: "https://wabi-insight.dev/problems/invalid-transition"),
            _ => NotFound(),
        };
    }

    [HttpPost]
    [ProducesResponseType<AccountProvisioningResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountProvisioningResponse>> Create([FromBody] CreateAdministratorRequest request, CancellationToken cancellationToken)
    {
        var result = await _accounts.CreateAsync(request.Email, request.FullName, request.PreferredLanguage ?? "en", cancellationToken);

        return result.Outcome switch
        {
            AdministratorAccountOutcome.Success => Created($"/api/platform/administrators/{result.AdministratorId}", ToResponse(result)),
            AdministratorAccountOutcome.EmailAlreadyRegistered => Problem(statusCode: StatusCodes.Status409Conflict, title: "Email already registered", type: "https://wabi-insight.dev/problems/email-already-registered"),
            _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid email address", type: "https://wabi-insight.dev/problems/invalid-email"),
        };
    }

    [HttpPost("{id:guid}/reset-password")]
    [ProducesResponseType<AccountProvisioningResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountProvisioningResponse>> ResetPassword(Guid id, CancellationToken cancellationToken)
    {
        var result = await _accounts.ResetPasswordAsync(id, cancellationToken);

        return result.Outcome switch
        {
            AdministratorAccountOutcome.Success => Ok(ToResponse(result)),
            AdministratorAccountOutcome.SelfResetNotAllowed => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Use change-password for your own account", type: "https://wabi-insight.dev/problems/self-reset"),
            _ => NotFound(),
        };
    }

    private static AccountProvisioningResponse ToResponse(AdministratorAccountResult result) =>
        new(result.AdministratorId!.Value, result.DeliveredTo!, true, result.EmailMessageId);
}
