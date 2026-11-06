using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Security;

namespace WabiInsight.Api.Security;

/// <summary>
/// Default request body limit for every endpoint (P12.1): JSON bodies are small, so anything above
/// <see cref="SecurityOptions.MaxRequestBodyBytes"/> is refused with 413 unless the action carries its own
/// <see cref="RequestSizeLimitAttribute"/> (logo and attachment uploads). A declared Content-Length is checked
/// here before any byte is read; bodies without a length are capped by the server through
/// <see cref="IHttpMaxRequestBodySizeFeature"/>.
/// </summary>
public sealed class RequestBodyLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOptions<SecurityOptions> _security;

    public RequestBodyLimitMiddleware(RequestDelegate next, IOptions<SecurityOptions> security)
    {
        _next = next;
        _security = security;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HasOwnPolicy(context.GetEndpoint()))
        {
            var limit = _security.Value.MaxRequestBodyBytes;
            if (context.Request.ContentLength is { } length && length > limit)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = StatusCodes.Status413PayloadTooLarge, Title = "Request body too large", Type = "payload-too-large", Detail = "request_body_too_large" });
                return;
            }

            var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false })
            {
                feature.MaxRequestBodySize = limit;
            }
        }

        await _next(context);
    }

    /// <summary>
    /// MVC keeps [RequestSizeLimit] / [DisableRequestSizeLimit] as filter factories on the action descriptor (the policy
    /// object only exists once the factory runs), so the attributes themselves are what identifies an endpoint with its own limit.
    /// </summary>
    internal static bool HasOwnPolicy(Endpoint? endpoint)
        => endpoint?.Metadata.GetMetadata<IRequestSizePolicy>() is not null
           || endpoint?.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>()?.FilterDescriptors.Any(f => f.Filter is IRequestSizePolicy or RequestSizeLimitAttribute or DisableRequestSizeLimitAttribute) == true;
}
