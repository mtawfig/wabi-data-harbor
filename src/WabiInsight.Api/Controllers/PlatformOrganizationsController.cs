using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Authorization;
using WabiInsight.Api.Contracts;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Organizations;

namespace WabiInsight.Api.Controllers;

/// <summary>Organization lifecycle for Platform Administrators (specification §4.1, §6). Responses carry counts, never application names.</summary>
[ApiController]
[Route("api/platform/organizations")]
[Authorize(Policy = Policies.PlatformAdministrator)]
public sealed class PlatformOrganizationsController : ControllerBase
{
    private readonly IOrganizationService _organizations;

    public PlatformOrganizationsController(IOrganizationService organizations)
    {
        _organizations = organizations;
    }

    [HttpGet]
    [ProducesResponseType<PagedResult<OrganizationDetail>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<OrganizationDetail>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] string? search = null, [FromQuery] OrganizationStatus? status = null, CancellationToken cancellationToken = default)
    {
        return Ok(await _organizations.ListAsync(new OrganizationListQuery(page, pageSize, search, status), cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<OrganizationDetail>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationDetail>> Get(Guid id, CancellationToken cancellationToken)
    {
        var organization = await _organizations.GetAsync(id, cancellationToken);
        return organization is null ? NotFound() : Ok(organization);
    }

    [HttpPost]
    [ProducesResponseType<OrganizationDetail>(StatusCodes.Status201Created)]
    public async Task<ActionResult<OrganizationDetail>> Create([FromBody] CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        var result = await _organizations.CreateAsync(new CreateOrganizationCommand(
            request.OrganizationCode, request.OrganizationName, request.OrganizationNameAr, request.Description, request.ContactEmail, request.ContactPhone,
            request.CountryCode, request.TimeZone, request.DefaultLanguage, request.InitialAdministratorEmail, request.InitialAdministratorFullName), cancellationToken);

        if (result.Outcome != OrganizationOperationOutcome.Success)
        {
            return ToProblem(result);
        }

        Response.Headers.Append("X-Delivered-To", result.DeliveredTo);
        return Created($"/api/platform/organizations/{result.Organization!.OrganizationId}", result.Organization);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<OrganizationDetail>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationDetail>> Update(Guid id, [FromBody] UpdateOrganizationRequest request, CancellationToken cancellationToken)
    {
        var result = await _organizations.UpdateAsync(id, new UpdateOrganizationCommand(
            request.OrganizationName, request.OrganizationNameAr, request.Description, request.ContactEmail, request.ContactPhone, request.CountryCode, request.TimeZone, request.DefaultLanguage), cancellationToken);
        return result.Outcome == OrganizationOperationOutcome.Success ? Ok(result.Organization) : ToProblem(result);
    }

    [HttpPost("{id:guid}/status")]
    [ProducesResponseType<OrganizationDetail>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationDetail>> SetStatus(Guid id, [FromBody] SetOrganizationStatusRequest request, CancellationToken cancellationToken)
    {
        if (!EnumInput.TryParse<OrganizationStatus>(request.Status, out var status))
        {
            ModelState.AddModelError(nameof(request.Status), "status_invalid");
            return ValidationProblem(ModelState);
        }

        var result = await _organizations.SetStatusAsync(id, status, request.Reason, cancellationToken);
        return result.Outcome == OrganizationOperationOutcome.Success ? Ok(result.Organization) : ToProblem(result);
    }

    [HttpPost("{id:guid}/initial-administrator/resend")]
    [ProducesResponseType<OrganizationProvisioningResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationProvisioningResponse>> ResendInitialAdministrator(Guid id, CancellationToken cancellationToken)
    {
        var result = await _organizations.ResendInitialAdministratorAsync(id, cancellationToken);
        return result.Outcome == OrganizationOperationOutcome.Success ? Ok(new OrganizationProvisioningResponse(id, result.DeliveredTo!)) : ToProblem(result);
    }

    private ObjectResult ToProblem(OrganizationOperationResult result)
    {
        var (status, title, code) = result.Outcome switch
        {
            OrganizationOperationOutcome.NotFound => (StatusCodes.Status404NotFound, "Organization not found", "organization-not-found"),
            OrganizationOperationOutcome.CodeAlreadyExists => (StatusCodes.Status409Conflict, "Organization code already exists", "organization-code-exists"),
            OrganizationOperationOutcome.EmailAlreadyRegistered => (StatusCodes.Status409Conflict, "Email already registered", "email-already-registered"),
            OrganizationOperationOutcome.InvalidStatusTransition => (StatusCodes.Status409Conflict, "Invalid status transition", "organization-status-transition"),
            _ => (StatusCodes.Status400BadRequest, "Validation failed", "organization-validation"),
        };

        return Problem(statusCode: status, title: title, detail: string.Join(", ", result.Errors), type: $"https://wabi-insight.dev/problems/{code}");
    }
}
