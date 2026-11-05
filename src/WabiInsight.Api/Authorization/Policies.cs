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
                .RequireClaim(WabiClaims.Role, OrganizationAdminRole));

        return services;
    }
}
