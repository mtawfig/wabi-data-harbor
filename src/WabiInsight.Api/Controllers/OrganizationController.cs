using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Authorization;
using WabiInsight.Api.Contracts;
using WabiInsight.Core.Organizations;

namespace WabiInsight.Api.Controllers;

/// <summary>The signed-in user's own organization. The organization comes from the tenant context, never from the request.</summary>
[ApiController]
[Route("api/organization")]
[Authorize(Policy = Policies.OrganizationUser)]
public sealed class OrganizationController : ControllerBase
{
    private readonly IOrganizationService _organizations;

    public OrganizationController(IOrganizationService organizations)
    {
        _organizations = organizations;
    }

    [HttpGet]
    [ProducesResponseType<OrganizationProfile>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationProfile>> Get(CancellationToken cancellationToken)
    {
        return Ok(await _organizations.GetCurrentAsync(cancellationToken));
    }

    [HttpPut]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    [ProducesResponseType<OrganizationProfile>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationProfile>> Update([FromBody] UpdateOrganizationProfileRequest request, CancellationToken cancellationToken)
    {
        var result = await _organizations.UpdateCurrentAsync(new UpdateOrganizationProfileCommand(
            request.OrganizationName, request.OrganizationNameAr, request.Description, request.ContactEmail, request.ContactPhone, request.TimeZone, request.DefaultLanguage), cancellationToken);

        if (result.Outcome != OrganizationOperationOutcome.Success)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Validation failed", detail: string.Join(", ", result.Errors), type: "https://wabi-insight.dev/problems/organization-validation");
        }

        return Ok(await _organizations.GetCurrentAsync(cancellationToken));
    }
}
