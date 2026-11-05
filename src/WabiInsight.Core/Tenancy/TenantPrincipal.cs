namespace WabiInsight.Core.Tenancy;

/// <summary>
/// Trusted identity resolved from the authentication cookie and configuration records.
/// Never built from request input. Immutable once set for the request.
/// </summary>
public sealed record TenantPrincipal
{
    public static readonly TenantPrincipal Anonymous = new()
    {
        PrincipalType = PrincipalType.Anonymous,
    };

    public PrincipalType PrincipalType { get; init; }

    /// <summary>Set only for organization users.</summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>Organization user id (OrganizationUsers.UserId) when PrincipalType is OrganizationUser.</summary>
    public Guid? UserId { get; init; }

    /// <summary>Administrator id (Administrators.AdministratorId) when PrincipalType is PlatformAdministrator.</summary>
    public Guid? AdministratorId { get; init; }

    public Guid? DepartmentId { get; init; }

    public Guid? SectionId { get; init; }

    public IReadOnlySet<string> Roles { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    public string DisplayName { get; init; } = string.Empty;

    public static TenantPrincipal ForPlatformAdministrator(Guid administratorId, string displayName) => new()
    {
        PrincipalType = PrincipalType.PlatformAdministrator,
        AdministratorId = administratorId,
        DisplayName = displayName,
    };

    public static TenantPrincipal ForOrganizationUser(
        Guid organizationId,
        Guid userId,
        string displayName,
        IEnumerable<string>? roles = null,
        Guid? departmentId = null,
        Guid? sectionId = null) => new()
    {
        PrincipalType = PrincipalType.OrganizationUser,
        OrganizationId = organizationId,
        UserId = userId,
        DisplayName = displayName,
        Roles = new HashSet<string>(roles ?? [], StringComparer.Ordinal),
        DepartmentId = departmentId,
        SectionId = sectionId,
    };
}
