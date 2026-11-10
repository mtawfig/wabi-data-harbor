using Microsoft.Extensions.Primitives;

namespace WabiInsight.Api.Security;

/// <summary>
/// Defensive response headers on every API response (P12.1). The Angular application is served by the reverse
/// proxy or static host with its own policy (see docs/DEPLOYMENT.md); the API only ever returns JSON, problem
/// details and authorized file downloads, so it can be strict: nothing may frame it, nothing may be sniffed, API
/// answers are never cached by shared caches, and JSON responses carry a no-source Content-Security-Policy.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var response = ((HttpContext)state).Response;
            var headers = response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            headers["X-Permitted-Cross-Domain-Policies"] = "none";
            if (!headers.ContainsKey("Cache-Control"))
            {
                // Authenticated data must not land in shared caches; downloads set their own headers before this runs.
                headers["Cache-Control"] = "no-store";
            }

            var contentType = response.ContentType ?? string.Empty;
            headers["Content-Security-Policy"] = contentType.Contains("json", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(contentType)
                ? "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'"
                : contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)
                    ? Hosting.WebAppHosting.ShellContentSecurityPolicy // the web app's page, when the API serves it (Web:RootPath)
                    : "frame-ancestors 'none'";
            headers.Remove("Server");
            headers.Remove("X-Powered-By");
            return Task.CompletedTask;
        }, context);
        return _next(context);
    }
}
