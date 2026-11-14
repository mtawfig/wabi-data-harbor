namespace WabiInsight.Core.Entities.Platform;

/// <summary>
/// A service plan the platform offers (P13.46). Platform-level: it has no <c>OrganizationId</c>; an organization
/// points at one plan through <see cref="Organization.ServicePlanId"/>. The plan sets how much the organization's
/// AI requests may cost in a calendar month, in the providers' price currency (USD).
/// </summary>
public class ServicePlan
{
    public Guid PlanId { get; set; }

    /// <summary>Stable, unique, upper-case code such as STARTER; labels are the two names.</summary>
    public string PlanCode { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    /// <summary>Most the organization's AI requests may cost in one calendar month (its own time zone); null means no limit.</summary>
    public decimal? MonthlyAiSpendLimit { get; set; }

    /// <summary>An inactive plan stays on the organizations that have it but cannot be given to another.</summary>
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedByAdministratorId { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
