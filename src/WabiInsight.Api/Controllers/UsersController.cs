using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Authorization;
using WabiInsight.Api.Contracts;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Organizations;

namespace WabiInsight.Api.Controllers;

/// <summary>
/// Organization user management for Organization Administrators (specification §9). The organization is the
/// administrator's own (tenant context); temporary passwords are emailed and never returned.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize(Policy = Policies.OrganizationAdmin)]
public sealed class UsersController : ControllerBase
{
    private readonly IOrganizationUserService _users;

    public UsersController(IOrganizationUserService users)
    {
        _users = users;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<OrganizationUserDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] OrganizationUserStatus? status = null,
        [FromQuery] Guid? departmentId = null,
        [FromQuery] string? roleCode = null,
        CancellationToken cancellationToken = default)
        => Ok(await _users.ListAsync(new OrganizationUserListQuery(page, pageSize, search, status, departmentId, roleCode), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrganizationUserDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var user = await _users.GetAsync(id, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost]
    public async Task<ActionResult<OrganizationUserDto>> Create([FromBody] CreateOrganizationUserRequest request, CancellationToken cancellationToken)
    {
        var (user, deliveredTo) = await _users.CreateAsync(new CreateOrganizationUserCommand(
            request.Email, request.FullName, request.EmployeeNumber, request.PhoneNumber, request.DepartmentId, request.SectionId, request.PreferredLanguage ?? "en", request.RoleCodes ?? []), cancellationToken);
        Response.Headers.Append("X-Delivered-To", deliveredTo);
        return Created($"/api/users/{user.UserId}", user);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OrganizationUserDto>> Update(Guid id, [FromBody] UpdateOrganizationUserRequest request, CancellationToken cancellationToken)
        => Ok(await _users.UpdateAsync(id, new UpdateOrganizationUserCommand(
            request.FullName, request.EmployeeNumber, request.PhoneNumber, request.DepartmentId, request.SectionId, request.PreferredLanguage, request.RoleCodes ?? []), cancellationToken));

    [HttpPost("{id:guid}/status")]
    public async Task<ActionResult<OrganizationUserDto>> SetStatus(Guid id, [FromBody] SetUserStatusRequest request, CancellationToken cancellationToken)
    {
        if (!EnumInput.TryParse<OrganizationUserStatus>(request.Status, out var status))
        {
            ModelState.AddModelError(nameof(request.Status), "status_invalid");
            return ValidationProblem(ModelState);
        }

        return Ok(await _users.SetStatusAsync(id, status, cancellationToken));
    }

    [HttpPost("{id:guid}/reset-password")]
    public async Task<ActionResult<UserProvisioningResponse>> ResetPassword(Guid id, CancellationToken cancellationToken)
        => Ok(new UserProvisioningResponse(id, await _users.ResetPasswordAsync(id, cancellationToken), true));
}
