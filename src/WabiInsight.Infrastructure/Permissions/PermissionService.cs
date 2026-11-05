using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Permissions;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Permissions;
using WabiInsight.Core.Security;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Permissions;

/// <summary>
/// Table, column and row permissions of an organization's roles (specification §10). Organization Administrators
/// act through the tenant filter; the support executor reaches <see cref="SetObjectGrantForOrganizationAsync"/>
/// under an approved grant with explicit organization scope. Management is automatic and never edited here.
/// </summary>
public sealed class PermissionService : IPermissionService
{
    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;

    public PermissionService(ConfigurationDbContext context, ITenantContext tenantContext, IAuditWriter audit, IClock clock)
    {
        _context = context;
        _tenantContext = tenantContext;
        _audit = audit;
        _clock = clock;
    }

    public async Task<RoleMatrixDto> GetRoleMatrixAsync(Guid roleId, Guid applicationId, CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        var role = await _context.Roles.AsNoTracking().SingleOrDefaultAsync(r => r.RoleId == roleId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "role_not_found");
        if (!await _context.Applications.AnyAsync(a => a.ApplicationId == applicationId, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.NotFound, "application_not_found");
        }

        var objects = await _context.DatabaseObjects.AsNoTracking().Where(o => o.ApplicationId == applicationId && o.IsPresent).OrderBy(o => o.SchemaName).ThenBy(o => o.ObjectName).ToListAsync(cancellationToken);
        var objectIds = objects.Select(o => o.ObjectId).ToList();
        var columns = await _context.DatabaseColumns.AsNoTracking().Where(c => objectIds.Contains(c.ObjectId) && c.IsPresent).OrderBy(c => c.OrdinalPosition).ToListAsync(cancellationToken);
        var tables = await _context.RoleTablePermissions.AsNoTracking().Where(p => p.RoleId == roleId && objectIds.Contains(p.ObjectId)).ToDictionaryAsync(p => p.ObjectId, cancellationToken);
        var granted = await _context.RoleColumnPermissions.AsNoTracking().Where(p => p.RoleId == roleId && objectIds.Contains(p.ObjectId)).Select(p => p.ColumnId).ToListAsync(cancellationToken);
        var grantedSet = granted.ToHashSet();
        var rows = await _context.RoleRowPermissions.AsNoTracking().Where(p => p.RoleId == roleId && objectIds.Contains(p.ObjectId)).ToDictionaryAsync(p => p.ObjectId, p => p.Scope, cancellationToken);
        var mappings = await _context.RowScopeColumnMappings.AsNoTracking().Where(m => objectIds.Contains(m.ObjectId)).ToDictionaryAsync(m => m.ObjectId, cancellationToken);

        var isManagement = role.IsSystem && role.RoleCode == SystemRoles.Management;
        var result = objects.Select(o =>
        {
            var table = tables.GetValueOrDefault(o.ObjectId);
            var mapping = mappings.GetValueOrDefault(o.ObjectId);
            return new ObjectGrantDto(
                o.ObjectId, o.ApplicationId, o.SchemaName, o.ObjectName, o.ObjectType, o.DisplayNameEn, o.DisplayNameAr, o.IsApproved, o.IsPresent,
                isManagement ? o.IsApproved : table is not null,
                isManagement || (table?.AllColumns ?? false),
                isManagement ? RowScope.All : rows.TryGetValue(o.ObjectId, out var scope) ? scope : null,
                columns.Where(c => c.ObjectId == o.ObjectId).Select(c => new ColumnGrantDto(c.ColumnId, c.ColumnName, c.DataType, c.Classification, c.IsApproved && (isManagement || (table?.AllColumns ?? false) || grantedSet.Contains(c.ColumnId)))).ToList(),
                Availability(mapping));
        }).ToList();
        return new RoleMatrixDto(role.RoleId, role.RoleCode, isManagement, applicationId, result);
    }

    public async Task<ObjectGrantDto> SetObjectGrantAsync(Guid roleId, Guid objectId, SetObjectGrantCommand command, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireAdmin();
        return await SetObjectGrantForOrganizationAsync(organizationId, roleId, objectId, command, cancellationToken);
    }

    /// <summary>Explicitly scoped write shared with the support executor (platform principal under a grant).</summary>
    public async Task<ObjectGrantDto> SetObjectGrantForOrganizationAsync(Guid organizationId, Guid roleId, Guid objectId, SetObjectGrantCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var roles = Scoped(_context.Roles);
        var role = await roles.AsNoTracking().SingleOrDefaultAsync(r => r.OrganizationId == organizationId && r.RoleId == roleId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "role_not_found");
        if (role.IsSystem && role.RoleCode == SystemRoles.Management)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "management_role_automatic");
        }

        var entity = await Scoped(_context.DatabaseObjects).AsNoTracking().SingleOrDefaultAsync(o => o.OrganizationId == organizationId && o.ObjectId == objectId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "object_not_found");
        if (!await Scoped(_context.Applications).AnyAsync(a => a.OrganizationId == organizationId && a.ApplicationId == entity.ApplicationId && !a.IsBuiltIn, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "application_builtin_protected");
        }

        var table = await Scoped(_context.RoleTablePermissions).SingleOrDefaultAsync(p => p.RoleId == roleId && p.ObjectId == objectId, cancellationToken);
        var existingColumns = await Scoped(_context.RoleColumnPermissions).Where(p => p.RoleId == roleId && p.ObjectId == objectId).ToListAsync(cancellationToken);
        var row = await Scoped(_context.RoleRowPermissions).SingleOrDefaultAsync(p => p.RoleId == roleId && p.ObjectId == objectId, cancellationToken);
        var before = new { granted = table is not null, allColumns = table?.AllColumns, columns = existingColumns.Count, scope = row?.Scope };

        if (!command.Granted)
        {
            if (table is not null)
            {
                _context.RoleTablePermissions.Remove(table);
            }

            _context.RoleColumnPermissions.RemoveRange(existingColumns);
            if (row is not null)
            {
                _context.RoleRowPermissions.Remove(row);
            }
        }
        else
        {
            if (!entity.IsApproved || !entity.IsPresent)
            {
                throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "object_not_approved");
            }

            if (command.RowScope is null)
            {
                throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "row_scope_required");
            }

            var principal = _tenantContext.Principal;
            if (table is null)
            {
                table = new RoleTableGrant { PermissionId = Ids.New(), OrganizationId = organizationId, RoleId = roleId, ObjectId = objectId, GrantedAt = _clock.UtcNow, GrantedByUserId = principal.UserId, GrantedByAdministratorId = principal.AdministratorId };
                _context.RoleTablePermissions.Add(table);
            }

            table.AllColumns = command.AllColumns;
            _context.RoleColumnPermissions.RemoveRange(existingColumns);
            if (!command.AllColumns)
            {
                var requested = command.ColumnIds.Distinct().ToList();
                var valid = await Scoped(_context.DatabaseColumns).AsNoTracking().Where(c => c.ObjectId == objectId && c.IsPresent && c.IsApproved && requested.Contains(c.ColumnId)).Select(c => c.ColumnId).ToListAsync(cancellationToken);
                if (valid.Count != requested.Count)
                {
                    throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "column_not_approved");
                }

                if (valid.Count == 0)
                {
                    throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "columns_required");
                }

                foreach (var columnId in valid)
                {
                    _context.RoleColumnPermissions.Add(new RoleColumnGrant { OrganizationId = organizationId, RoleId = roleId, ObjectId = objectId, ColumnId = columnId });
                }
            }

            if (row is null)
            {
                _context.RoleRowPermissions.Add(new RoleRowGrant { OrganizationId = organizationId, RoleId = roleId, ObjectId = objectId, Scope = command.RowScope.Value });
            }
            else
            {
                row.Scope = command.RowScope.Value;
            }
        }

        _audit.Append(AuditEvents.PermissionChanged, "Role", roleId.ToString(), new { objectId, entity.SchemaName, entity.ObjectName, before, after = new { command.Granted, command.AllColumns, columns = command.AllColumns ? -1 : command.ColumnIds.Count, scope = command.RowScope } }, organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        var matrix = await GetRoleMatrixForOrganizationAsync(organizationId, roleId, entity.ApplicationId, cancellationToken);
        return matrix.Objects.Single(o => o.ObjectId == objectId);
    }

    public async Task<RowMappingDto> GetRowMappingAsync(Guid objectId, CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        if (!await _context.DatabaseObjects.AnyAsync(o => o.ObjectId == objectId, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.NotFound, "object_not_found");
        }

        var mapping = await _context.RowScopeColumnMappings.AsNoTracking().SingleOrDefaultAsync(m => m.ObjectId == objectId, cancellationToken);
        var columns = await _context.DatabaseColumns.AsNoTracking().Where(c => c.ObjectId == objectId && c.IsPresent).OrderBy(c => c.OrdinalPosition)
            .Select(c => new ColumnGrantDto(c.ColumnId, c.ColumnName, c.DataType, c.Classification, c.IsApproved)).ToListAsync(cancellationToken);
        return new RowMappingDto(objectId, mapping?.EmployeeColumnId, mapping?.DepartmentColumnId, mapping?.SectionColumnId, columns);
    }

    public async Task<RowMappingDto> SetRowMappingAsync(Guid objectId, SetRowMappingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var organizationId = RequireAdmin();
        var entity = await _context.DatabaseObjects.AsNoTracking().SingleOrDefaultAsync(o => o.ObjectId == objectId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "object_not_found");
        var ids = new[] { command.EmployeeColumnId, command.DepartmentColumnId, command.SectionColumnId }.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var valid = await _context.DatabaseColumns.AsNoTracking().CountAsync(c => c.ObjectId == objectId && c.IsPresent && ids.Contains(c.ColumnId), cancellationToken);
        if (valid != ids.Count)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "mapping_column_invalid");
        }

        var mapping = await _context.RowScopeColumnMappings.SingleOrDefaultAsync(m => m.ObjectId == objectId, cancellationToken);
        if (mapping is null)
        {
            mapping = new RowScopeColumnMapping { ObjectId = objectId, OrganizationId = organizationId };
            _context.RowScopeColumnMappings.Add(mapping);
        }

        mapping.EmployeeColumnId = command.EmployeeColumnId;
        mapping.DepartmentColumnId = command.DepartmentColumnId;
        mapping.SectionColumnId = command.SectionColumnId;
        mapping.UpdatedAt = _clock.UtcNow;
        mapping.UpdatedByUserId = _tenantContext.Principal.UserId;
        _audit.Append(AuditEvents.RowMappingChanged, "DatabaseObject", objectId.ToString(), new { entity.SchemaName, entity.ObjectName, command.EmployeeColumnId, command.DepartmentColumnId, command.SectionColumnId }, organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return await GetRowMappingAsync(objectId, cancellationToken);
    }

    private async Task<RoleMatrixDto> GetRoleMatrixForOrganizationAsync(Guid organizationId, Guid roleId, Guid applicationId, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsPlatformAdministrator)
        {
            return await GetRoleMatrixAsync(roleId, applicationId, cancellationToken);
        }

        // Platform principal under a grant: same projection with explicit organization scope.
        var role = await _context.Roles.AsPlatformScope(_tenantContext).AsNoTracking().SingleAsync(r => r.OrganizationId == organizationId && r.RoleId == roleId, cancellationToken);
        var objects = await _context.DatabaseObjects.AsPlatformScope(_tenantContext).AsNoTracking().Where(o => o.OrganizationId == organizationId && o.ApplicationId == applicationId && o.IsPresent).ToListAsync(cancellationToken);
        var objectIds = objects.Select(o => o.ObjectId).ToList();
        var columns = await _context.DatabaseColumns.AsPlatformScope(_tenantContext).AsNoTracking().Where(c => objectIds.Contains(c.ObjectId) && c.IsPresent).ToListAsync(cancellationToken);
        var tables = await _context.RoleTablePermissions.AsPlatformScope(_tenantContext).AsNoTracking().Where(p => p.RoleId == roleId).ToDictionaryAsync(p => p.ObjectId, cancellationToken);
        var granted = (await _context.RoleColumnPermissions.AsPlatformScope(_tenantContext).AsNoTracking().Where(p => p.RoleId == roleId).Select(p => p.ColumnId).ToListAsync(cancellationToken)).ToHashSet();
        var rows = await _context.RoleRowPermissions.AsPlatformScope(_tenantContext).AsNoTracking().Where(p => p.RoleId == roleId).ToDictionaryAsync(p => p.ObjectId, p => p.Scope, cancellationToken);
        var mappings = await _context.RowScopeColumnMappings.AsPlatformScope(_tenantContext).AsNoTracking().Where(m => objectIds.Contains(m.ObjectId)).ToDictionaryAsync(m => m.ObjectId, cancellationToken);
        var result = objects.Select(o =>
        {
            var table = tables.GetValueOrDefault(o.ObjectId);
            return new ObjectGrantDto(o.ObjectId, o.ApplicationId, o.SchemaName, o.ObjectName, o.ObjectType, o.DisplayNameEn, o.DisplayNameAr, o.IsApproved, o.IsPresent, table is not null, table?.AllColumns ?? false,
                rows.TryGetValue(o.ObjectId, out var scope) ? scope : null,
                columns.Where(c => c.ObjectId == o.ObjectId).OrderBy(c => c.OrdinalPosition).Select(c => new ColumnGrantDto(c.ColumnId, c.ColumnName, c.DataType, c.Classification, c.IsApproved && ((table?.AllColumns ?? false) || granted.Contains(c.ColumnId)))).ToList(),
                Availability(mappings.GetValueOrDefault(o.ObjectId)));
        }).ToList();
        return new RoleMatrixDto(role.RoleId, role.RoleCode, false, applicationId, result);
    }

    internal static MappingAvailability Availability(RowScopeColumnMapping? mapping)
        => mapping is null ? MappingAvailability.None : new MappingAvailability(mapping.EmployeeColumnId.HasValue, mapping.DepartmentColumnId.HasValue, mapping.SectionColumnId.HasValue);

    private IQueryable<T> Scoped<T>(IQueryable<T> query)
        where T : class, ITenantOwned
        => _tenantContext.IsPlatformAdministrator ? query.AsPlatformScope(_tenantContext) : query;

    private Guid RequireAdmin()
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        if (!_tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin))
        {
            throw new TenantContextException("Only organization administrators can manage permissions.");
        }

        return organizationId;
    }
}
