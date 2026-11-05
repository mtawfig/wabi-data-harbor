using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Permissions;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Permissions;
using WabiInsight.Core.Security;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Permissions;

/// <summary>
/// Effective permissions (specification §10, reviews B12/B13). Order of evaluation: organization boundary
/// (the user's own organization, never widened), approved and active applications, approved and present
/// objects and columns, then the union of the user's active roles combined by <see cref="PermissionCombiner"/>.
/// Management contributes All on every approved object; OrganizationAdmin alone contributes nothing.
/// </summary>
public sealed class PermissionResolver : IPermissionResolver
{
    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;

    public PermissionResolver(ConfigurationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public Task<EffectivePermissions> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        return ResolveForUserAsync(organizationId, _tenantContext.Principal.UserId!.Value, cancellationToken);
    }

    public Task<EffectivePermissions> ResolveForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        if (_tenantContext.Principal.UserId != userId && !_tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin))
        {
            throw new TenantContextException("Only organization administrators can preview another user's permissions.");
        }

        return ResolveForUserAsync(organizationId, userId, cancellationToken);
    }

    public async Task<EffectivePermissions> ResolveForUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsPlatformAdministrator && _tenantContext.OrganizationId != organizationId)
        {
            throw new TenantContextException("Cross-organization permission resolution is not allowed.");
        }

        var users = Scoped(_context.OrganizationUsers);
        var user = await users.AsNoTracking().SingleOrDefaultAsync(u => u.OrganizationId == organizationId && u.UserId == userId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "user_not_found");

        var departmentCode = user.DepartmentId is { } d ? await Scoped(_context.Departments).AsNoTracking().Where(x => x.DepartmentId == d).Select(x => x.DepartmentCode).SingleOrDefaultAsync(cancellationToken) : null;
        var sectionCode = user.SectionId is { } s ? await Scoped(_context.Sections).AsNoTracking().Where(x => x.SectionId == s).Select(x => x.SectionCode).SingleOrDefaultAsync(cancellationToken) : null;
        var context = new RowScopeContext(user.EmployeeNumber, departmentCode, sectionCode);

        var roleIds = await Scoped(_context.UserRoles).AsNoTracking().Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync(cancellationToken);
        var roles = await Scoped(_context.Roles).AsNoTracking().Where(r => roleIds.Contains(r.RoleId) && r.IsActive).ToListAsync(cancellationToken);
        var roleCodes = roles.Select(r => r.RoleCode).OrderBy(c => c, StringComparer.Ordinal).ToList();
        var activeRoleIds = roles.Select(r => r.RoleId).ToHashSet();
        var isManagement = roles.Any(r => r.IsSystem && r.RoleCode == SystemRoles.Management);
        var roleNames = roles.ToDictionary(r => r.RoleId, r => r.RoleCode);

        var applications = await Scoped(_context.Applications).AsNoTracking().Where(a => a.OrganizationId == organizationId && a.IsActive && a.IsApproved && !a.IsBuiltIn).ToListAsync(cancellationToken);
        var applicationIds = applications.Select(a => a.ApplicationId).ToList();
        var objects = await Scoped(_context.DatabaseObjects).AsNoTracking().Where(o => applicationIds.Contains(o.ApplicationId) && o.IsApproved && o.IsPresent).OrderBy(o => o.SchemaName).ThenBy(o => o.ObjectName).ToListAsync(cancellationToken);
        var objectIds = objects.Select(o => o.ObjectId).ToList();
        var columns = await Scoped(_context.DatabaseColumns).AsNoTracking().Where(c => objectIds.Contains(c.ObjectId) && c.IsApproved && c.IsPresent).OrderBy(c => c.OrdinalPosition).ToListAsync(cancellationToken);
        var tables = await Scoped(_context.RoleTablePermissions).AsNoTracking().Where(p => activeRoleIds.Contains(p.RoleId) && objectIds.Contains(p.ObjectId)).ToListAsync(cancellationToken);
        var columnGrants = await Scoped(_context.RoleColumnPermissions).AsNoTracking().Where(p => activeRoleIds.Contains(p.RoleId) && objectIds.Contains(p.ObjectId)).ToListAsync(cancellationToken);
        var rowGrants = await Scoped(_context.RoleRowPermissions).AsNoTracking().Where(p => activeRoleIds.Contains(p.RoleId) && objectIds.Contains(p.ObjectId)).ToListAsync(cancellationToken);
        var mappings = await Scoped(_context.RowScopeColumnMappings).AsNoTracking().Where(m => objectIds.Contains(m.ObjectId)).ToDictionaryAsync(m => m.ObjectId, cancellationToken);

        var result = new List<EffectiveObjectAccess>();
        foreach (var o in objects)
        {
            var approvedColumns = columns.Where(c => c.ObjectId == o.ObjectId).ToList();
            var approvedIds = approvedColumns.Select(c => c.ColumnId).ToHashSet();
            var grants = tables.Where(t => t.ObjectId == o.ObjectId)
                .Select(t => new RoleObjectGrant(t.RoleId, t.AllColumns, columnGrants.Where(c => c.RoleId == t.RoleId && c.ObjectId == o.ObjectId).Select(c => c.ColumnId).ToHashSet(), rowGrants.FirstOrDefault(r => r.RoleId == t.RoleId && r.ObjectId == o.ObjectId)?.Scope))
                .ToList();
            var via = grants.Select(g => roleNames[g.RoleId]).ToList();
            if (isManagement)
            {
                grants.Add(new RoleObjectGrant(Guid.Empty, true, new HashSet<Guid>(), RowScope.All));
                via.Add(SystemRoles.Management);
            }

            if (grants.Count == 0)
            {
                continue;
            }

            var combined = PermissionCombiner.Combine(grants, approvedIds, PermissionService.Availability(mappings.GetValueOrDefault(o.ObjectId)));
            if (combined.ColumnIds.Count == 0)
            {
                continue;
            }

            var application = applications.Single(a => a.ApplicationId == o.ApplicationId);
            result.Add(new EffectiveObjectAccess(
                o.ApplicationId, application.ApplicationCode, o.ObjectId, o.SchemaName, o.ObjectName, o.ObjectType, o.DisplayNameEn, o.DisplayNameAr,
                approvedColumns.Where(c => combined.ColumnIds.Contains(c.ColumnId)).Select(c => new EffectiveColumn(c.ColumnId, c.ColumnName, c.DataType, c.DisplayNameEn, c.DisplayNameAr, c.Classification)).ToList(),
                combined.RowScopes.OrderBy(x => x).ToList(),
                combined.UnmappedScopes.OrderBy(x => x).ToList(),
                combined.YieldsRows,
                via.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList()));
        }

        return new EffectivePermissions(userId, organizationId, roleCodes, context, result);
    }

    private IQueryable<T> Scoped<T>(IQueryable<T> query)
        where T : class, ITenantOwned
        => _tenantContext.IsPlatformAdministrator ? query.AsPlatformScope(_tenantContext) : query;
}
