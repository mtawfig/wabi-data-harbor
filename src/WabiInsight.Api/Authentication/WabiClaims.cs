using System.Globalization;
using System.Security.Claims;
using WabiInsight.Core.Authentication;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Api.Authentication;

/// <summary>Claim types carried by the session cookie. All values originate from configuration records.</summary>
public static class WabiClaims
{
    public const string Subject = "sub";
    public const string PrincipalType = "principal_type";
    public const string Organization = "org";
    public const string Name = "name";
    public const string Email = "email";
    public const string SecurityStamp = "security_stamp";
    public const string MustChangePassword = "must_change_password";
    public const string ValidatedAt = "validated_at";
    public const string Language = "lang";
    public const string Department = "dept";
    public const string Section = "sect";
    public const string Role = "role";

    public const string AuthenticationType = "WabiInsightCookie";

    public static ClaimsPrincipal ToClaimsPrincipal(AuthenticatedAccount account, DateTimeOffset validatedAt)
    {
        ArgumentNullException.ThrowIfNull(account);
        var p = account.Principal;
        var id = (p.AdministratorId ?? p.UserId)!.Value;

        var claims = new List<Claim>
        {
            new(Subject, id.ToString()),
            new(PrincipalType, p.PrincipalType.ToString()),
            new(Name, p.DisplayName),
            new(Email, account.Email),
            new(SecurityStamp, account.SecurityStamp),
            new(MustChangePassword, account.MustChangePassword ? "true" : "false"),
            new(ValidatedAt, validatedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            new(Language, account.PreferredLanguage),
        };

        if (p.OrganizationId is { } org)
        {
            claims.Add(new Claim(Organization, org.ToString()));
        }

        if (p.DepartmentId is { } dept)
        {
            claims.Add(new Claim(Department, dept.ToString()));
        }

        if (p.SectionId is { } sect)
        {
            claims.Add(new Claim(Section, sect.ToString()));
        }

        claims.AddRange(p.Roles.Select(role => new Claim(Role, role)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType, Name, Role));
    }

    /// <summary>
    /// The same session with another interface language: every other claim is kept as it is, including the security stamp
    /// and the time it was last checked, so changing the language never refreshes a session that is due to end.
    /// </summary>
    public static ClaimsPrincipal WithLanguage(ClaimsPrincipal user, string language)
    {
        ArgumentNullException.ThrowIfNull(user);
        var claims = user.Claims.Where(claim => claim.Type != Language).Select(claim => new Claim(claim.Type, claim.Value)).Append(new Claim(Language, language));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType, Name, Role));
    }

    /// <summary>Rebuilds the trusted principal from cookie claims. Returns null for anything malformed.</summary>
    public static TenantPrincipal? ToTenantPrincipal(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        if (!Guid.TryParse(user.FindFirstValue(Subject), out var id)
            || !Enum.TryParse<PrincipalType>(user.FindFirstValue(PrincipalType), out var type))
        {
            return null;
        }

        var name = user.FindFirstValue(Name) ?? string.Empty;

        switch (type)
        {
            case Core.Tenancy.PrincipalType.PlatformAdministrator:
                return user.FindFirst(Organization) is null
                    ? TenantPrincipal.ForPlatformAdministrator(id, name)
                    : null;

            case Core.Tenancy.PrincipalType.OrganizationUser:
                if (!Guid.TryParse(user.FindFirstValue(Organization), out var org))
                {
                    return null;
                }

                Guid? dept = Guid.TryParse(user.FindFirstValue(Department), out var d) ? d : null;
                Guid? sect = Guid.TryParse(user.FindFirstValue(Section), out var s) ? s : null;
                var roles = user.FindAll(Role).Select(c => c.Value);
                return TenantPrincipal.ForOrganizationUser(org, id, name, roles, dept, sect);

            default:
                return null;
        }
    }

    public static bool MustChangePasswordClaim(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return string.Equals(user.FindFirstValue(MustChangePassword), "true", StringComparison.Ordinal);
    }
}
