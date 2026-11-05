namespace WabiInsight.Core.Tenancy;

/// <summary>Scoped per request. Starts anonymous; can be initialized exactly once.</summary>
public sealed class TenantContext : ITenantContext, ITenantContextInitializer
{
    private TenantPrincipal _principal = TenantPrincipal.Anonymous;
    private bool _initialized;

    public TenantPrincipal Principal => _principal;

    public void Initialize(TenantPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (_initialized)
        {
            throw new TenantContextException("The tenant context has already been initialized for this scope.");
        }

        if (principal.PrincipalType == PrincipalType.OrganizationUser && principal.OrganizationId is null)
        {
            throw new TenantContextException("An organization user principal must carry an OrganizationId.");
        }

        if (principal.PrincipalType == PrincipalType.PlatformAdministrator && principal.OrganizationId is not null)
        {
            throw new TenantContextException("A platform administrator principal must not carry an OrganizationId.");
        }

        _principal = principal;
        _initialized = true;
    }
}
