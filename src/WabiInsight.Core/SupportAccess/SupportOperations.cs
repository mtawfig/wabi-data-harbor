namespace WabiInsight.Core.SupportAccess;

/// <summary>
/// The typed operation catalog (specification §16.2). Every entry names what it may touch; there is no
/// global tenant-administration grant. Operations whose feature ships later are proposable but not executable yet.
/// </summary>
public static class SupportOperations
{
    public const string CreateOrganizationUser = "CreateOrganizationUser";
    public const string ResetOrganizationUserPassword = "ResetOrganizationUserPassword";
    public const string ConfigureRole = "ConfigureRole";
    public const string UpdateTablePermissions = "UpdateTablePermissions";
    public const string RefreshMetadata = "RefreshMetadata";
    public const string DiagnoseApplicationConnection = "DiagnoseApplicationConnection";
    public const string UpdateOrganizationSettings = "UpdateOrganizationSettings";

    public static readonly IReadOnlyList<SupportOperationDefinition> Catalog =
    [
        new(CreateOrganizationUser, SupportTargetType.AssignableRole, Executable: true, "Create an organization user. The roles are limited to the ones listed in the grant; with no role listed the user is created without roles. The temporary password is emailed to the user."),
        new(ResetOrganizationUserPassword, SupportTargetType.OrganizationUser, Executable: true, "Issue a new temporary password to the listed user(s)."),
        new(ConfigureRole, SupportTargetType.RoleOrNew, Executable: true, "Create or update the listed role(s): code, names, description, active flag."),
        new(UpdateOrganizationSettings, SupportTargetType.None, Executable: true, "Update the organization profile: names, description, contacts, time zone, default language."),
        new(UpdateTablePermissions, SupportTargetType.Role, Executable: true, "Change table, column and row permissions of the listed role(s) on one object."),
        new(RefreshMetadata, SupportTargetType.Application, Executable: true, "Refresh the metadata catalog of the listed application(s)."),
        new(DiagnoseApplicationConnection, SupportTargetType.Application, Executable: true, "Connectivity and read-only checks on the listed application(s); never reads business rows."),
    ];

    public static SupportOperationDefinition? Find(string? operation)
        => operation is null ? null : Catalog.FirstOrDefault(o => string.Equals(o.Code, operation, StringComparison.Ordinal));
}

public enum SupportTargetType
{
    /// <summary>Organization-level operation; no object references.</summary>
    None = 0,
    OrganizationUser = 1,
    Role = 2,
    /// <summary>A role id, or the literal "new" to create one.</summary>
    RoleOrNew = 3,
    Application = 4,
    /// <summary>
    /// Role ids the operation may assign to what it creates. The list is optional (none: no role may be assigned) and
    /// the execution names no target: the roles it asks for are checked against the list instead.
    /// </summary>
    AssignableRole = 5,
}

public sealed record SupportOperationDefinition(string Code, SupportTargetType TargetType, bool Executable, string Description)
{
    /// <summary>True when a proposal must list at least one target and every execution names one of them.</summary>
    public bool RequiresTarget => TargetType is not (SupportTargetType.None or SupportTargetType.AssignableRole);
}
