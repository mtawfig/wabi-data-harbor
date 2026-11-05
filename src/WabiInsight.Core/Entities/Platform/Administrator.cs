using WabiInsight.Core.Common;

namespace WabiInsight.Core.Entities.Platform;

/// <summary>
/// Platform Administrator of the company operating Wabi Insight. Lives in its own table and has
/// no OrganizationId by design (specification §5.1).
/// </summary>
public class Administrator : AuditableEntity
{
    public Guid AdministratorId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string NormalizedEmail { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public bool MustChangePassword { get; set; } = true;

    public bool IsBootstrapAdministrator { get; set; }

    public string SecurityStamp { get; set; } = string.Empty;

    public int FailedLoginCount { get; set; }

    public DateTimeOffset? LockoutEnd { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public DateTimeOffset? PasswordChangedAt { get; set; }

    public string PreferredLanguage { get; set; } = "en";

    public Guid? CreatedByAdministratorId { get; set; }
}
