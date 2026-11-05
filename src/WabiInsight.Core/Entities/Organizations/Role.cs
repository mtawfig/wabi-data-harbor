using WabiInsight.Core.Common;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Organizations;

/// <summary>Organization-scoped role. System roles (OrganizationAdmin, Management) are protected and provisioned at onboarding.</summary>
public class Role : AuditableEntity, ITenantOwned
{
    public Guid RoleId { get; set; }

    public Guid OrganizationId { get; set; }

    public string RoleCode { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsSystem { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Version of the default template this role was provisioned from; null for custom roles.</summary>
    public int? TemplateVersion { get; set; }
}

/// <summary>Assignment of an organization role to an organization user. Both must belong to the same organization (composite foreign keys).</summary>
public class UserRole : ITenantOwned
{
    public Guid OrganizationId { get; set; }

    public Guid UserId { get; set; }

    public Guid RoleId { get; set; }

    public DateTimeOffset AssignedAt { get; set; }

    public Guid? AssignedByUserId { get; set; }

    public Guid? AssignedByAdministratorId { get; set; }
}

/// <summary>Protected system roles and the templates provisioned for every new organization (specification §5.3, §5.4, §10).</summary>
public static class SystemRoles
{
    public const string OrganizationAdmin = "OrganizationAdmin";
    public const string Management = "Management";
    public const string Analyst = "Analyst";

    public const int TemplateVersion = 1;

    public static readonly IReadOnlyList<(string Code, string NameEn, string NameAr, bool IsSystem, string Description)> Templates =
    [
        (OrganizationAdmin, "Organization Administrator", "مدير المؤسسة", true, "Manages the organization's users, structure, roles, applications, metadata and settings."),
        (Management, "Management", "الإدارة", true, "Read-only access to all approved registered objects of the organization."),
        (Analyst, "Analyst", "محلّل", false, "Default business-data role; permissions are granted explicitly."),
    ];
}
