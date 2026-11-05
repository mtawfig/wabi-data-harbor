using WabiInsight.Core.Common;

namespace WabiInsight.Core.Entities.Platform;

public enum OrganizationStatus
{
    Active = 1,
    Suspended = 2,
    Deactivated = 3,
}

/// <summary>A customer organization (logical tenant). Created only by Platform Administrators.</summary>
public class Organization : AuditableEntity
{
    public Guid OrganizationId { get; set; }

    /// <summary>Immutable, globally unique, upper-case code such as ACME.</summary>
    public string OrganizationCode { get; set; } = string.Empty;

    public string OrganizationName { get; set; } = string.Empty;

    public string OrganizationNameAr { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? ContactEmail { get; set; }

    public string? ContactPhone { get; set; }

    public string? CountryCode { get; set; }

    public string TimeZone { get; set; } = "UTC";

    public string DefaultLanguage { get; set; } = "en";

    public OrganizationStatus Status { get; set; } = OrganizationStatus.Active;

    /// <summary>When true, a requester who is an OrganizationAdmin cannot approve their own support-access grant (review A12).</summary>
    public bool RequireSecondAdminApprovalForSupportAccess { get; set; }

    /// <summary>Provider this organization's AI requests use; null means the platform default (specification §18).</summary>
    public Guid? AiProviderConfigurationId { get; set; }

    public Guid? CreatedByAdministratorId { get; set; }

    public Guid? UpdatedByAdministratorId { get; set; }
}
