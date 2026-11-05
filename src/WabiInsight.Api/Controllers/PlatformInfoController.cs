using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Api.Contracts;
using WabiInsight.Core.Platform;

namespace WabiInsight.Api.Controllers;

/// <summary>
/// Public, non-sensitive product identity used by the Angular shell (name, code, languages).
/// It never exposes configuration, bootstrap state or organization data.
/// </summary>
[ApiController]
[Route("api/platform/info")]
[AllowAnonymous]
public sealed class PlatformInfoController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PlatformInfoResponse>(StatusCodes.Status200OK)]
    public ActionResult<PlatformInfoResponse> Get()
    {
        return Ok(new PlatformInfoResponse(
            ProductInfo.ProductName,
            ProductInfo.ProductNameArabic,
            ProductInfo.PlatformCode,
            ProductInfo.Tagline,
            ProductInfo.TaglineArabic,
            ProductInfo.Version,
            ProductInfo.SupportedLanguages));
    }
}
