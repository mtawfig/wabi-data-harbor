using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Authentication;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Authentication;

/// <summary>
/// Reads the stored state of an account. Runs with the platform-scope bypass because it executes before
/// (or independently of) the request's tenant context, and returns only the fields needed for the session.
/// A lockout is not checked here (P13.25): it answers wrong passwords at sign-in and must not end a session that
/// is already open, or anyone who knows an e-mail address could sign its owner out with five wrong attempts.
/// Deactivation, suspension and password changes still end sessions (status and security stamp).
/// </summary>
public sealed class AccountResolver : IAccountResolver
{
    private readonly ConfigurationDbContext _context;

    public AccountResolver(ConfigurationDbContext context)
    {
        _context = context;
    }

    public async Task<ResolvedAccount> ResolveAsync(PrincipalType principalType, Guid principalId, CancellationToken cancellationToken = default)
    {
        return principalType switch
        {
            PrincipalType.PlatformAdministrator => await ResolveAdministratorAsync(principalId, cancellationToken),
            PrincipalType.OrganizationUser => await ResolveOrganizationUserAsync(principalId, cancellationToken),
            _ => new ResolvedAccount(null, AccountDenyReason.NotFound),
        };
    }

    private async Task<ResolvedAccount> ResolveAdministratorAsync(Guid id, CancellationToken cancellationToken)
    {
        var admin = await _context.Administrators.AsNoTracking()
            .SingleOrDefaultAsync(a => a.AdministratorId == id, cancellationToken);

        if (admin is null)
        {
            return new ResolvedAccount(null, AccountDenyReason.NotFound);
        }

        if (!admin.IsActive)
        {
            return new ResolvedAccount(null, AccountDenyReason.Inactive);
        }

        return new ResolvedAccount(ToAccount(admin), AccountDenyReason.None);
    }

    private async Task<ResolvedAccount> ResolveOrganizationUserAsync(Guid id, CancellationToken cancellationToken)
    {
        // Explicit, field-restricted cross-tenant read: the request has no tenant context yet.
        var row = await _context.OrganizationUsers.AsNoTracking().IgnoreQueryFilters()
            .Where(u => u.UserId == id)
            .Select(u => new
            {
                User = u,
                OrganizationStatus = _context.Organizations.Where(o => o.OrganizationId == u.OrganizationId).Select(o => (OrganizationStatus?)o.Status).FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return new ResolvedAccount(null, AccountDenyReason.NotFound);
        }

        if (row.User.Status != OrganizationUserStatus.Active)
        {
            return new ResolvedAccount(null, AccountDenyReason.Inactive);
        }

        if (row.OrganizationStatus != OrganizationStatus.Active)
        {
            return new ResolvedAccount(null, AccountDenyReason.OrganizationSuspended);
        }

        var roles = await RoleCodesAsync(_context, row.User, cancellationToken);
        return new ResolvedAccount(ToAccount(row.User, roles), AccountDenyReason.None);
    }

    /// <summary>Role codes of an organization user, read with an explicit organization predicate (no tenant context during authentication).</summary>
    internal static Task<List<string>> RoleCodesAsync(ConfigurationDbContext context, OrganizationUser user, CancellationToken cancellationToken)
    {
        return context.UserRoles.IgnoreQueryFilters()
            .Where(ur => ur.OrganizationId == user.OrganizationId && ur.UserId == user.UserId)
            .Join(context.Roles.IgnoreQueryFilters().Where(r => r.OrganizationId == user.OrganizationId && r.IsActive), ur => ur.RoleId, r => r.RoleId, (_, r) => r.RoleCode)
            .OrderBy(code => code)
            .ToListAsync(cancellationToken);
    }

    internal static AuthenticatedAccount ToAccount(Administrator admin) => new(
        TenantPrincipal.ForPlatformAdministrator(admin.AdministratorId, admin.FullName),
        admin.Email,
        admin.SecurityStamp,
        admin.MustChangePassword,
        admin.PreferredLanguage);

    internal static AuthenticatedAccount ToAccount(OrganizationUser user, IEnumerable<string> roles) => new(
        TenantPrincipal.ForOrganizationUser(user.OrganizationId, user.UserId, user.FullName, roles, user.DepartmentId, user.SectionId),
        user.Email,
        user.SecurityStamp,
        user.MustChangePassword,
        user.PreferredLanguage);
}
