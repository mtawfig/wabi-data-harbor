using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Authorization;
using WabiInsight.Api.Contracts;
using WabiInsight.Core.Organizations;

namespace WabiInsight.Api.Controllers;

/// <summary>Departments of the signed-in user's organization. Reads for any organization user; writes for OrganizationAdmin.</summary>
[ApiController]
[Route("api/departments")]
[Authorize(Policy = Policies.OrganizationUser)]
public sealed class DepartmentsController : ControllerBase
{
    private readonly IOrganizationStructureService _structure;

    public DepartmentsController(IOrganizationStructureService structure)
    {
        _structure = structure;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DepartmentDto>>> List([FromQuery] bool includeInactive = true, CancellationToken cancellationToken = default)
        => Ok(await _structure.ListDepartmentsAsync(includeInactive, cancellationToken));

    [HttpPost]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    public async Task<ActionResult<DepartmentDto>> Create([FromBody] SaveDepartmentRequest request, CancellationToken cancellationToken)
    {
        var created = await _structure.CreateDepartmentAsync(new SaveDepartmentCommand(request.DepartmentCode, request.DepartmentNameEn, request.DepartmentNameAr, request.IsActive), cancellationToken);
        return Created($"/api/departments/{created.DepartmentId}", created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    public async Task<ActionResult<DepartmentDto>> Update(Guid id, [FromBody] SaveDepartmentRequest request, CancellationToken cancellationToken)
        => Ok(await _structure.UpdateDepartmentAsync(id, new SaveDepartmentCommand(request.DepartmentCode, request.DepartmentNameEn, request.DepartmentNameAr, request.IsActive), cancellationToken));
}

[ApiController]
[Route("api/sections")]
[Authorize(Policy = Policies.OrganizationUser)]
public sealed class SectionsController : ControllerBase
{
    private readonly IOrganizationStructureService _structure;

    public SectionsController(IOrganizationStructureService structure)
    {
        _structure = structure;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SectionDto>>> List([FromQuery] Guid? departmentId = null, [FromQuery] bool includeInactive = true, CancellationToken cancellationToken = default)
        => Ok(await _structure.ListSectionsAsync(departmentId, includeInactive, cancellationToken));

    [HttpPost]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    public async Task<ActionResult<SectionDto>> Create([FromBody] SaveSectionRequest request, CancellationToken cancellationToken)
    {
        var created = await _structure.CreateSectionAsync(new SaveSectionCommand(request.DepartmentId, request.SectionCode, request.SectionNameEn, request.SectionNameAr, request.IsActive), cancellationToken);
        return Created($"/api/sections/{created.SectionId}", created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    public async Task<ActionResult<SectionDto>> Update(Guid id, [FromBody] SaveSectionRequest request, CancellationToken cancellationToken)
        => Ok(await _structure.UpdateSectionAsync(id, new SaveSectionCommand(request.DepartmentId, request.SectionCode, request.SectionNameEn, request.SectionNameAr, request.IsActive), cancellationToken));
}

[ApiController]
[Route("api/roles")]
[Authorize(Policy = Policies.OrganizationAdmin)]
public sealed class RolesController : ControllerBase
{
    private readonly IRoleService _roles;

    public RolesController(IRoleService roles)
    {
        _roles = roles;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> List(CancellationToken cancellationToken) => Ok(await _roles.ListAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<RoleDto>> Create([FromBody] SaveRoleRequest request, CancellationToken cancellationToken)
    {
        var created = await _roles.CreateAsync(new SaveRoleCommand(request.RoleCode, request.NameEn, request.NameAr, request.Description, request.IsActive), cancellationToken);
        return Created($"/api/roles/{created.RoleId}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RoleDto>> Update(Guid id, [FromBody] SaveRoleRequest request, CancellationToken cancellationToken)
        => Ok(await _roles.UpdateAsync(id, new SaveRoleCommand(request.RoleCode, request.NameEn, request.NameAr, request.Description, request.IsActive), cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _roles.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}

/// <summary>Platform-managed default role templates used when a new organization is created.</summary>
[ApiController]
[Route("api/platform/role-templates")]
[Authorize(Policy = Policies.PlatformAdministrator)]
public sealed class PlatformRoleTemplatesController : ControllerBase
{
    private readonly IRoleTemplateService _templates;

    public PlatformRoleTemplatesController(IRoleTemplateService templates)
    {
        _templates = templates;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoleTemplateDto>>> List(CancellationToken cancellationToken) => Ok(await _templates.ListAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<RoleTemplateDto>> Create([FromBody] SaveRoleTemplateRequest request, CancellationToken cancellationToken)
    {
        var created = await _templates.CreateAsync(new SaveRoleTemplateCommand(request.RoleCode, request.NameEn, request.NameAr, request.Description, request.IsActive, request.SortOrder), cancellationToken);
        return Created($"/api/platform/role-templates/{created.TemplateId}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RoleTemplateDto>> Update(Guid id, [FromBody] SaveRoleTemplateRequest request, CancellationToken cancellationToken)
        => Ok(await _templates.UpdateAsync(id, new SaveRoleTemplateCommand(request.RoleCode, request.NameEn, request.NameAr, request.Description, request.IsActive, request.SortOrder), cancellationToken));
}
