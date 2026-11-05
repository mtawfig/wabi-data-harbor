using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Authorization;
using WabiInsight.Api.Contracts;
using WabiInsight.Core.Permissions;

namespace WabiInsight.Api.Controllers;

/// <summary>Table, column and row permissions of the organization's roles (specification §10) and effective-permission views.</summary>
[ApiController]
[Route("api/permissions")]
[Authorize(Policy = Policies.OrganizationUser)]
public sealed class PermissionsController : ControllerBase
{
    private readonly IPermissionService _permissions;
    private readonly IPermissionResolver _resolver;

    public PermissionsController(IPermissionService permissions, IPermissionResolver resolver)
    {
        _permissions = permissions;
        _resolver = resolver;
    }

    /// <summary>What the signed-in user may query: approved objects, columns and row scopes after combination.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<EffectivePermissions>> Me(CancellationToken cancellationToken) => Ok(await _resolver.ResolveAsync(cancellationToken));

    [HttpGet("users/{userId:guid}/effective")]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    public async Task<ActionResult<EffectivePermissions>> Effective(Guid userId, CancellationToken cancellationToken) => Ok(await _resolver.ResolveForUserAsync(userId, cancellationToken));

    [HttpGet("roles/{roleId:guid}/applications/{applicationId:guid}")]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    public async Task<ActionResult<RoleMatrixDto>> Matrix(Guid roleId, Guid applicationId, CancellationToken cancellationToken) => Ok(await _permissions.GetRoleMatrixAsync(roleId, applicationId, cancellationToken));

    [HttpPut("roles/{roleId:guid}/objects/{objectId:guid}")]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    public async Task<ActionResult<ObjectGrantDto>> SetGrant(Guid roleId, Guid objectId, [FromBody] SetObjectGrantRequest request, CancellationToken cancellationToken)
        => Ok(await _permissions.SetObjectGrantAsync(roleId, objectId, new SetObjectGrantCommand(request.Granted, request.AllColumns, request.ColumnIds ?? [], request.RowScope), cancellationToken));

    [HttpGet("objects/{objectId:guid}/row-mapping")]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    public async Task<ActionResult<RowMappingDto>> Mapping(Guid objectId, CancellationToken cancellationToken) => Ok(await _permissions.GetRowMappingAsync(objectId, cancellationToken));

    [HttpPut("objects/{objectId:guid}/row-mapping")]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    public async Task<ActionResult<RowMappingDto>> SetMapping(Guid objectId, [FromBody] SetRowMappingRequest request, CancellationToken cancellationToken)
        => Ok(await _permissions.SetRowMappingAsync(objectId, new SetRowMappingCommand(request.EmployeeColumnId, request.DepartmentColumnId, request.SectionColumnId), cancellationToken));
}
