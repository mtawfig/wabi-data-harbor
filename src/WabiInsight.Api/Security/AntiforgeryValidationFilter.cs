using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WabiInsight.Api.Security;

/// <summary>
/// Default-on antiforgery for controllers: every unsafe request (POST/PUT/PATCH/DELETE) must carry a valid
/// X-XSRF-TOKEN header matching the antiforgery cookie, unless the action or controller opts out with
/// [IgnoreAntiforgeryToken] (read-only or pre-session endpoints). Failures produce a 400 problem response.
/// </summary>
public sealed class AntiforgeryValidationFilter : IAsyncAuthorizationFilter
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    private readonly IAntiforgery _antiforgery;

    public AntiforgeryValidationFilter(IAntiforgery antiforgery)
    {
        _antiforgery = antiforgery;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (SafeMethods.Contains(context.HttpContext.Request.Method))
        {
            return;
        }

        var metadata = context.ActionDescriptor.EndpointMetadata.OfType<IAntiforgeryMetadata>().LastOrDefault();
        if (metadata is { RequiresValidation: false })
        {
            return;
        }

        try
        {
            await _antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Type = "https://wabi-insight.dev/problems/antiforgery",
                Title = "Antiforgery validation failed",
                Status = StatusCodes.Status400BadRequest,
                Detail = "The request must include a valid X-XSRF-TOKEN header. Call GET /api/auth/csrf first.",
                Instance = context.HttpContext.Request.Path,
            })
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentTypes = { "application/problem+json" },
            };
        }
    }
}
