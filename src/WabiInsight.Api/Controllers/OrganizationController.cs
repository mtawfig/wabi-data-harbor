using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Authorization;
using WabiInsight.Api.Contracts;
using WabiInsight.Api.Security;
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

    /// <summary>The caller's own roles with their names, so the profile shows "Management" / "الإدارة" rather than codes.</summary>
    [HttpGet("my-roles")]
    public async Task<ActionResult<IReadOnlyList<MyRoleDto>>> MyRoles(CancellationToken cancellationToken) => Ok(await _organizations.GetMyRolesAsync(cancellationToken));

    [HttpPut]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    [ProducesResponseType<OrganizationProfile>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationProfile>> Update([FromBody] UpdateOrganizationProfileRequest request, CancellationToken cancellationToken)
    {
        var result = await _organizations.UpdateCurrentAsync(new UpdateOrganizationProfileCommand(
            request.OrganizationName, request.OrganizationNameAr, request.Description, request.ContactEmail, request.ContactPhone, request.TimeZone, request.DefaultLanguage, request.RequireSecondAdminApprovalForSupportAccess), cancellationToken);

        if (result.Outcome != OrganizationOperationOutcome.Success)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Validation failed", detail: string.Join(", ", result.Errors), type: "https://wabi-insight.dev/problems/organization-validation");
        }

        return Ok(await _organizations.GetCurrentAsync(cancellationToken));
    }

    /// <summary>
    /// The organization's logo as uploaded; 404 when none. Uploads are PNG or JPEG. A logo stored as SVG before that
    /// rule is still answered, but as an attachment under a sandbox policy: an image element shows it, and a browser
    /// that opens the address directly downloads it instead of running it in this site's origin.
    /// </summary>
    [HttpGet("logo")]
    public async Task<IActionResult> Logo(CancellationToken cancellationToken)
    {
        var logo = await _organizations.OpenLogoAsync(cancellationToken);
        if (logo is null)
        {
            return NotFound();
        }

        var raster = logo.ContentType is "image/png" or "image/jpeg";
        Response.Headers.ContentSecurityPolicy = SecurityHeadersMiddleware.SandboxedFilePolicy;
        if (!raster)
        {
            Response.Headers.ContentDisposition = "attachment; filename=\"logo\"";
        }

        return File(logo.Content, logo.ContentType);
    }

    /// <summary>Replaces the logo stamped on report pages (PNG or JPEG, at most 1 MB).</summary>
    [HttpPut("logo")]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> SetLogo(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return ValidationProblem("A file is required.");
        }

        await using var content = file.OpenReadStream();
        await _organizations.SetLogoAsync(new LogoUpload(file.FileName, file.ContentType, file.Length, content), cancellationToken);
        return NoContent();
    }

    [HttpDelete("logo")]
    [Authorize(Policy = Policies.OrganizationAdmin)]
    public async Task<IActionResult> RemoveLogo(CancellationToken cancellationToken)
    {
        await _organizations.RemoveLogoAsync(cancellationToken);
        return NoContent();
    }
}
