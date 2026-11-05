using WabiInsight.Core.Common;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Organizations;

public enum OrganizationUserStatus
{
    PendingInvitation = 1,
    Active = 2,
    Inactive = 3,
}

/// <summary>An employee account. Always belongs to exactly one organization (specification §9).</summary>
public class OrganizationUser : AuditableEntity, ITenantOwned
{
    public Guid UserId { get; set; }

    public Guid OrganizationId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string NormalizedEmail { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? EmployeeNumber { get; set; }

    public string? PhoneNumber { get; set; }

    /// <summary>Departments and Sections tables arrive in P2.2; the columns exist now so the shape is stable.</summary>
    public Guid? DepartmentId { get; set; }

    public Guid? SectionId { get; set; }

    public string PreferredLanguage { get; set; } = "en";

    public string? PreferredLocale { get; set; }

    public string? PreferredTimezone { get; set; }

    public OrganizationUserStatus Status { get; set; } = OrganizationUserStatus.PendingInvitation;

    public bool MustChangePassword { get; set; } = true;

    public string? PasswordHash { get; set; }

    public string SecurityStamp { get; set; } = string.Empty;

    public int FailedLoginCount { get; set; }

    public DateTimeOffset? LockoutEnd { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public DateTimeOffset? PasswordChangedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    /// <summary>Set only when created through onboarding or an approved support grant.</summary>
    public Guid? CreatedByAdministratorId { get; set; }

    public Guid? UpdatedByUserId { get; set; }
}
