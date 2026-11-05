namespace WabiInsight.Core.Tenancy;

/// <summary>
/// Marks an entity owned by exactly one organization. Every such entity gets a global EF query
/// filter on <see cref="OrganizationId"/> and a write-time ownership guard.
/// </summary>
public interface ITenantOwned
{
    Guid OrganizationId { get; }
}
