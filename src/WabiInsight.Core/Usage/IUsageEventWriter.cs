namespace WabiInsight.Core.Usage;

/// <summary>Records verified usage events. Attribution (organization, actor, department) comes from the tenant context unless overridden.</summary>
public interface IUsageEventWriter
{
    /// <summary>Adds the event to the current unit of work; the caller's SaveChanges persists it with the business change.</summary>
    void Append(string eventType, string? entityType = null, string? entityId = null, long quantity = 1, bool succeeded = true, object? metadata = null, Guid? organizationId = null, Tenancy.PrincipalType? actorType = null, Guid? actorId = null, Guid? departmentId = null);

    /// <summary>Persists a single event immediately (for events without an accompanying business change).</summary>
    Task AppendAndSaveAsync(string eventType, string? entityType = null, string? entityId = null, long quantity = 1, bool succeeded = true, object? metadata = null, Guid? organizationId = null, Tenancy.PrincipalType? actorType = null, Guid? actorId = null, Guid? departmentId = null, CancellationToken cancellationToken = default);
}
