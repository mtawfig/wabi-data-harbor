using WabiInsight.Api.Authentication;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Api.Authorization;

/// <summary>
/// Default-deny authorization: the fallback policy requires an authenticated session for every endpoint
/// without explicit metadata. Role names are the protected organization role codes (specification §5).
/// </summary>
public static class Policies
{
    public const string PlatformAdministrator = "PlatformAdministrator";
    public const string OrganizationUser = "OrganizationUser";
    public const string OrganizationAdmin = "OrganizationAdmin";
    /// <summary>Platform administrator with an explicit, unexpired support-mode session (specification §16.3).</summary>
    public const string SupportMode = "SupportMode";

    public const string OrganizationAdminRole = "OrganizationAdmin";
    public const string ManagementRole = "Management";

    public static IServiceCollection AddWabiAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy(PlatformAdministrator, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(WabiClaims.PrincipalType, nameof(PrincipalType.PlatformAdministrator)))
            .AddPolicy(OrganizationUser, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(WabiClaims.PrincipalType, nameof(PrincipalType.OrganizationUser))
                .RequireClaim(WabiClaims.Organization))
            .AddPolicy(OrganizationAdmin, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(WabiClaims.PrincipalType, nameof(PrincipalType.OrganizationUser))
                .RequireClaim(WabiClaims.Organization)
                .RequireClaim(WabiClaims.Role, OrganizationAdminRole))
            .AddPolicy(SupportMode, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(WabiClaims.PrincipalType, nameof(PrincipalType.PlatformAdministrator))
                .AddRequirements(new SupportModeRequirement()));
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, SupportModeHandler>();

        return services;
    }
}

/// <summary>Satisfied only while the caller has an active support session; the per-operation scope check happens in the authorizer.</summary>
public sealed class SupportModeRequirement : Microsoft.AspNetCore.Authorization.IAuthorizationRequirement;

public sealed class SupportModeHandler : Microsoft.AspNetCore.Authorization.AuthorizationHandler<SupportModeRequirement>
{
    private readonly Core.SupportAccess.ISupportAccessAuthorizer _authorizer;

    public SupportModeHandler(Core.SupportAccess.ISupportAccessAuthorizer authorizer)
    {
        _authorizer = authorizer;
    }

    protected override async Task HandleRequirementAsync(Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext context, SupportModeRequirement requirement)
    {
        if (await _authorizer.HasActiveSessionAsync())
        {
            context.Succeed(requirement);
        }
    }
}
