using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Authentication;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Authentication;

/// <summary>
/// Reads the stored state of an account. Runs with the platform-scope bypass because it executes before
/// (or independently of) the request's tenant context, and returns only the fields needed for the session.
/// </summary>
public sealed class AccountResolver : IAccountResolver
{
    private readonly ConfigurationDbContext _context;
    private readonly IClock _clock;

    public AccountResolver(ConfigurationDbContext context, IClock clock)
    {
        _context = context;
        _clock = clock;
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

        if (admin.LockoutEnd is { } end && end > _clock.UtcNow)
        {
            return new ResolvedAccount(null, AccountDenyReason.LockedOut);
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

        if (row.User.LockoutEnd is { } end && end > _clock.UtcNow)
        {
            return new ResolvedAccount(null, AccountDenyReason.LockedOut);
        }

        return new ResolvedAccount(ToAccount(row.User), AccountDenyReason.None);
    }

    internal static AuthenticatedAccount ToAccount(Administrator admin) => new(
        TenantPrincipal.ForPlatformAdministrator(admin.AdministratorId, admin.FullName),
        admin.Email,
        admin.SecurityStamp,
        admin.MustChangePassword,
        admin.PreferredLanguage);

    internal static AuthenticatedAccount ToAccount(OrganizationUser user) => new(
        TenantPrincipal.ForOrganizationUser(user.OrganizationId, user.UserId, user.FullName, roles: null, user.DepartmentId, user.SectionId),
        user.Email,
        user.SecurityStamp,
        user.MustChangePassword,
        user.PreferredLanguage);
}
