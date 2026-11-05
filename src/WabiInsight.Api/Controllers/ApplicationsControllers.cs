using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Authorization;
using WabiInsight.Api.Contracts;
using WabiInsight.Core.Applications;
using WabiInsight.Core.Metadata;
using WabiInsight.Core.Organizations;

namespace WabiInsight.Api.Controllers;

/// <summary>Applications (data sources) of the signed-in administrator's organization (specification §11). Connection strings are write-only.</summary>
[ApiController]
[Route("api/applications")]
[Authorize(Policy = Policies.OrganizationAdmin)]
public sealed class ApplicationsController : ControllerBase
{
    private readonly IApplicationService _applications;

    public ApplicationsController(IApplicationService applications)
    {
        _applications = applications;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApplicationDto>>> List([FromQuery] bool includeInactive = true, CancellationToken cancellationToken = default)
        => Ok(await _applications.ListAsync(includeInactive, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApplicationDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var application = await _applications.GetAsync(id, cancellationToken);
        return application is null ? NotFound() : Ok(application);
    }

    [HttpPost]
    public async Task<ActionResult<ApplicationDto>> Create([FromBody] CreateApplicationRequest request, CancellationToken cancellationToken)
    {
        var created = await _applications.CreateAsync(new SaveApplicationCommand(request.ApplicationCode, request.ApplicationName, request.ApplicationNameAr, request.Description, request.DatabaseType, request.ConnectionString, request.ConnectionHint), cancellationToken);
        return Created($"/api/applications/{created.ApplicationId}", created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApplicationDto>> Update(Guid id, [FromBody] UpdateApplicationRequest request, CancellationToken cancellationToken)
        => Ok(await _applications.UpdateAsync(id, new UpdateApplicationCommand(request.ApplicationName, request.ApplicationNameAr, request.Description, request.IsActive), cancellationToken));

    [HttpPut("{id:guid}/connection")]
    public async Task<ActionResult<ApplicationDto>> ReplaceConnection(Guid id, [FromBody] ReplaceConnectionRequest request, CancellationToken cancellationToken)
        => Ok(await _applications.ReplaceConnectionAsync(id, request.ConnectionString, request.ConnectionHint, cancellationToken));

    [HttpPost("test-connection")]
    public async Task<ActionResult<ConnectionTestResult>> TestConnection([FromBody] TestConnectionRequest request, CancellationToken cancellationToken)
        => Ok(await _applications.TestConnectionAsync(request.DatabaseType, request.ConnectionString, cancellationToken));

    [HttpPost("{id:guid}/test-connection")]
    public async Task<ActionResult<ConnectionTestResult>> TestStored(Guid id, CancellationToken cancellationToken)
        => Ok(await _applications.TestStoredConnectionAsync(id, cancellationToken));

    [HttpPost("{id:guid}/approval")]
    public async Task<ActionResult<ApplicationDto>> SetApproval(Guid id, [FromBody] SetApplicationApprovalRequest request, CancellationToken cancellationToken)
        => Ok(await _applications.SetApprovalAsync(id, request.IsApproved, cancellationToken));
}

/// <summary>Metadata catalog of the organization's applications (specification §17).</summary>
[ApiController]
[Route("api/metadata")]
[Authorize(Policy = Policies.OrganizationAdmin)]
public sealed class MetadataController : ControllerBase
{
    private readonly IMetadataService _metadata;

    public MetadataController(IMetadataService metadata)
    {
        _metadata = metadata;
    }

    [HttpPost("applications/{applicationId:guid}/refresh")]
    public async Task<ActionResult<MetadataRefreshResult>> Refresh(Guid applicationId, CancellationToken cancellationToken)
        => Ok(await _metadata.RefreshAsync(applicationId, cancellationToken));

    [HttpGet("applications/{applicationId:guid}/objects")]
    public async Task<ActionResult<PagedResult<ObjectSummaryDto>>> Objects(Guid applicationId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? search = null, [FromQuery] bool? approved = null, [FromQuery] bool includeRemoved = false, CancellationToken cancellationToken = default)
        => Ok(await _metadata.ListObjectsAsync(new ObjectListQuery(applicationId, page, pageSize, search, approved, includeRemoved), cancellationToken));

    [HttpGet("objects/{objectId:guid}")]
    public async Task<ActionResult<ObjectDetailDto>> GetObject(Guid objectId, CancellationToken cancellationToken)
    {
        var detail = await _metadata.GetObjectAsync(objectId, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPut("objects/{objectId:guid}")]
    public async Task<ActionResult<ObjectDetailDto>> UpdateObject(Guid objectId, [FromBody] UpdateObjectRequest request, CancellationToken cancellationToken)
        => Ok(await _metadata.UpdateObjectAsync(objectId, new UpdateObjectCommand(request.DisplayNameEn, request.DisplayNameAr, request.BusinessDescription, request.IsApproved), cancellationToken));

    [HttpPost("objects/{objectId:guid}/approve")]
    public async Task<ActionResult<ObjectDetailDto>> ApproveObject(Guid objectId, [FromBody] ApproveObjectRequest request, CancellationToken cancellationToken)
        => Ok(await _metadata.ApproveObjectWithColumnsAsync(objectId, request.Approve, cancellationToken));

    [HttpPut("columns/{columnId:guid}")]
    public async Task<ActionResult<ColumnDto>> UpdateColumn(Guid columnId, [FromBody] UpdateColumnRequest request, CancellationToken cancellationToken)
        => Ok(await _metadata.UpdateColumnAsync(columnId, new UpdateColumnCommand(request.DisplayNameEn, request.DisplayNameAr, request.BusinessDescription, request.Classification, request.IsApproved), cancellationToken));
}
