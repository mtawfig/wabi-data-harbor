using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Audit;

/// <summary>
/// Append-only security/audit record. The application never updates or deletes rows and the runtime
/// database login is denied UPDATE/DELETE on the table (scripts/sql/create-runtime-login.sql).
/// </summary>
public class AuditLog
{
    public Guid AuditLogId { get; set; }

    /// <summary>Null for platform-level events (bootstrap, organization creation, administrator changes).</summary>
    public Guid? OrganizationId { get; set; }

    public PrincipalType ActorType { get; set; }

    public Guid? ActorId { get; set; }

    public string ActorDisplayName { get; set; } = string.Empty;

    /// <summary>Stable code such as Bootstrap.Completed or Organization.Created.</summary>
    public string EventType { get; set; } = string.Empty;

    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    /// <summary>Old/new values as JSON. Secrets are never written here.</summary>
    public string? DetailsJson { get; set; }

    public string? CorrelationId { get; set; }

    public string? IpAddress { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
