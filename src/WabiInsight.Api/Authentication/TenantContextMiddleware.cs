using WabiInsight.Core.Tenancy;

namespace WabiInsight.Api.Authentication;

/// <summary>
/// Initializes the scoped tenant context from the authenticated cookie principal. Runs after
/// authentication and before authorization so every handler sees the trusted identity.
/// </summary>
public sealed class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    public TenantContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContextInitializer initializer)
    {
        var principal = WabiClaims.ToTenantPrincipal(context.User);
        if (principal is not null)
        {
            initializer.Initialize(principal);
        }

        await _next(context);
    }
}
