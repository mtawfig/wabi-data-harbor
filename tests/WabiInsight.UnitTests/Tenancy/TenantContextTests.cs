using WabiInsight.Core.Tenancy;

namespace WabiInsight.UnitTests.Tenancy;

public class TenantContextTests
{
    private static readonly Guid OrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void StartsAnonymous_WithNoOrganization()
    {
        ITenantContext context = new TenantContext();

        Assert.False(context.IsAuthenticated);
        Assert.Null(context.OrganizationId);
        Assert.Throws<TenantContextException>(() => context.RequireOrganizationId());
    }

    [Fact]
    public void OrganizationUser_ExposesOrganization()
    {
        var context = new TenantContext();
        context.Initialize(TenantPrincipal.ForOrganizationUser(OrgId, UserId, "Alice", ["Analyst"]));
        ITenantContext tenant = context;

        Assert.True(tenant.IsOrganizationUser);
        Assert.Equal(OrgId, tenant.RequireOrganizationId());
        Assert.Contains("Analyst", tenant.Principal.Roles);
    }

    [Fact]
    public void PlatformAdministrator_HasNoOrganization()
    {
        var context = new TenantContext();
        context.Initialize(TenantPrincipal.ForPlatformAdministrator(UserId, "Admin"));
        ITenantContext tenant = context;

        Assert.True(tenant.IsPlatformAdministrator);
        Assert.Null(tenant.OrganizationId);
        Assert.Throws<TenantContextException>(() => tenant.RequireOrganizationId());
    }

    [Fact]
    public void Initialize_Twice_IsRejected()
    {
        var context = new TenantContext();
        context.Initialize(TenantPrincipal.ForPlatformAdministrator(UserId, "Admin"));

        Assert.Throws<TenantContextException>(() => context.Initialize(TenantPrincipal.ForPlatformAdministrator(UserId, "Admin")));
    }

    [Fact]
    public void OrganizationUser_WithoutOrganization_IsRejected()
    {
        var context = new TenantContext();
        var principal = new TenantPrincipal { PrincipalType = PrincipalType.OrganizationUser, UserId = UserId };

        Assert.Throws<TenantContextException>(() => context.Initialize(principal));
    }

    [Fact]
    public void PlatformAdministrator_WithOrganization_IsRejected()
    {
        var context = new TenantContext();
        var principal = new TenantPrincipal { PrincipalType = PrincipalType.PlatformAdministrator, AdministratorId = UserId, OrganizationId = OrgId };

        Assert.Throws<TenantContextException>(() => context.Initialize(principal));
    }
}
