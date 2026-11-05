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
        new(CreateOrganizationUser, SupportTargetType.None, Executable: true, "Create an organization user with the listed roles; the temporary password is emailed to the user."),
        new(ResetOrganizationUserPassword, SupportTargetType.OrganizationUser, Executable: true, "Issue a new temporary password to the listed user(s)."),
        new(ConfigureRole, SupportTargetType.RoleOrNew, Executable: true, "Create or update the listed role(s): code, names, description, active flag."),
        new(UpdateOrganizationSettings, SupportTargetType.None, Executable: true, "Update the organization profile: names, description, contacts, time zone, default language."),
        new(UpdateTablePermissions, SupportTargetType.Role, Executable: false, "Change table/column/row permissions of the listed role(s). Executable from Phase 6."),
        new(RefreshMetadata, SupportTargetType.Application, Executable: false, "Refresh the metadata catalog of the listed application(s). Executable from Phase 6."),
        new(DiagnoseApplicationConnection, SupportTargetType.Application, Executable: false, "Connectivity and object-existence checks on the listed application(s); never reads business rows. Executable from Phase 6."),
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
}

public sealed record SupportOperationDefinition(string Code, SupportTargetType TargetType, bool Executable, string Description);
