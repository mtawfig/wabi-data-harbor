using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Permissions;

/// <summary>Row scopes (specification §10). Organization isolation is evaluated first and is never widened by any scope.</summary>
public enum RowScope
{
    All = 1,
    Department = 2,
    Section = 3,
    Self = 4,
}

/// <summary>Read access of one role to one approved registered object. Columns are either all approved columns or the listed ones.</summary>
public class RoleTableGrant : ITenantOwned
{
    public Guid PermissionId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid RoleId { get; set; }

    public Guid ObjectId { get; set; }

    /// <summary>True: every approved, present column of the object; false: only the rows in RoleColumnPermissions.</summary>
    public bool AllColumns { get; set; }

    public DateTimeOffset GrantedAt { get; set; }

    public Guid? GrantedByUserId { get; set; }

    public Guid? GrantedByAdministratorId { get; set; }
}

public class RoleColumnGrant : ITenantOwned
{
    public Guid OrganizationId { get; set; }

    public Guid RoleId { get; set; }

    public Guid ObjectId { get; set; }

    public Guid ColumnId { get; set; }
}

/// <summary>Row scope of one role on one object. Several roles combine with OR; a scope without its mapping yields no rows (review B13).</summary>
public class RoleRowGrant : ITenantOwned
{
    public Guid OrganizationId { get; set; }

    public Guid RoleId { get; set; }

    public Guid ObjectId { get; set; }

    public RowScope Scope { get; set; }
}

/// <summary>Per registered object: which columns hold the employee identifier, department code and section code.</summary>
public class RowScopeColumnMapping : ITenantOwned
{
    public Guid ObjectId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid? EmployeeColumnId { get; set; }

    public Guid? DepartmentColumnId { get; set; }

    public Guid? SectionColumnId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedByUserId { get; set; }
}
