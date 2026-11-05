using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Insights;

/// <summary>
/// Grants one approved Platform Insights metric to one organization role (specification §12.2). The
/// OrganizationAdmin role never needs rows here: it reads every approved metric automatically.
/// </summary>
public class RoleMetricGrant : ITenantOwned
{
    public Guid OrganizationId { get; set; }

    public Guid RoleId { get; set; }

    public Guid InsightsMetricId { get; set; }

    public DateTimeOffset GrantedAt { get; set; }

    public Guid? GrantedByUserId { get; set; }
}
