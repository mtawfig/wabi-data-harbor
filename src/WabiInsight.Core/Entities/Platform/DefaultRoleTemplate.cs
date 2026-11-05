namespace WabiInsight.Core.Entities.Platform;

/// <summary>
/// Platform-managed role templates copied into every new organization (specification §10). Changing a
/// template never modifies roles that existing organizations already hold; <see cref="Version"/> is
/// recorded on provisioned roles for future explicit template updates.
/// </summary>
public class DefaultRoleTemplate
{
    public Guid TemplateId { get; set; }

    public string RoleCode { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>System templates (OrganizationAdmin, Management) are always provisioned and cannot be removed.</summary>
    public bool IsSystem { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public int Version { get; set; } = 1;

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedByAdministratorId { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
