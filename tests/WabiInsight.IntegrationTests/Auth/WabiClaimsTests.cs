using WabiInsight.Api.Authentication;
using WabiInsight.Core.Authentication;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.IntegrationTests.Auth;

public class WabiClaimsTests
{
    [Fact]
    public void OrganizationUser_RoundTrips_ThroughClaims()
    {
        var org = Guid.NewGuid();
        var user = Guid.NewGuid();
        var dept = Guid.NewGuid();
        var account = new AuthenticatedAccount(
            TenantPrincipal.ForOrganizationUser(org, user, "Alice", ["Analyst", "OrganizationAdmin"], dept, null),
            "alice@org-a.test", "stamp", false, "ar");

        var principal = WabiClaims.ToTenantPrincipal(WabiClaims.ToClaimsPrincipal(account, DateTimeOffset.UtcNow));

        Assert.NotNull(principal);
        Assert.Equal(PrincipalType.OrganizationUser, principal.PrincipalType);
        Assert.Equal(org, principal.OrganizationId);
        Assert.Equal(user, principal.UserId);
        Assert.Equal(dept, principal.DepartmentId);
        Assert.Equal(["Analyst", "OrganizationAdmin"], principal.Roles.Order());
    }

    [Fact]
    public void PlatformAdministrator_WithOrganizationClaim_IsRejected()
    {
        var account = new AuthenticatedAccount(TenantPrincipal.ForPlatformAdministrator(Guid.NewGuid(), "Admin"), "a@b.test", "stamp", false, "en");
        var claims = WabiClaims.ToClaimsPrincipal(account, DateTimeOffset.UtcNow);
        var identity = (System.Security.Claims.ClaimsIdentity)claims.Identity!;
        identity.AddClaim(new System.Security.Claims.Claim(WabiClaims.Organization, Guid.NewGuid().ToString()));

        Assert.Null(WabiClaims.ToTenantPrincipal(claims));
    }

    [Fact]
    public void OrganizationUser_WithoutOrganizationClaim_IsRejected()
    {
        var account = new AuthenticatedAccount(TenantPrincipal.ForOrganizationUser(Guid.NewGuid(), Guid.NewGuid(), "Bob"), "b@b.test", "stamp", false, "en");
        var claims = WabiClaims.ToClaimsPrincipal(account, DateTimeOffset.UtcNow);
        var identity = (System.Security.Claims.ClaimsIdentity)claims.Identity!;
        identity.RemoveClaim(identity.FindFirst(WabiClaims.Organization));

        Assert.Null(WabiClaims.ToTenantPrincipal(claims));
    }
}
