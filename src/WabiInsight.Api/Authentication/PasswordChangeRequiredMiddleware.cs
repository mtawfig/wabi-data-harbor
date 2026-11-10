using Microsoft.AspNetCore.Mvc;

namespace WabiInsight.Api.Authentication;

/// <summary>
/// Forces the first-login password change: while the session carries must_change_password, only the
/// endpoints needed to change it (and to leave) are reachable.
/// </summary>
public sealed class PasswordChangeRequiredMiddleware
{
    public const string ProblemType = "https://wabi-insight.dev/problems/password-change-required";

    private static readonly HashSet<string> AllowedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/me",
        "/api/auth/csrf",
        "/api/auth/logout",
        "/api/auth/change-password",
    };

    private readonly RequestDelegate _next;

    public PasswordChangeRequiredMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only data is gated: the web app's own files (served by the API when Web:RootPath is set) must still load so
        // the user can reach the change-password page.
        if (context.User.Identity?.IsAuthenticated == true
            && WabiClaims.MustChangePasswordClaim(context.User)
            && context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && !AllowedPaths.Contains(context.Request.Path.Value ?? string.Empty))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Type = ProblemType,
                Title = "Password change required",
                Status = StatusCodes.Status403Forbidden,
                Detail = "You must change your password before continuing.",
                Instance = context.Request.Path,
            });
            return;
        }

        await _next(context);
    }
}
