namespace WabiInsight.Core.Common;

/// <summary>Base for editable aggregates: UTC timestamps and optimistic concurrency.</summary>
public abstract class AuditableEntity
{
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>SQL Server rowversion; EF Core uses it as the concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];
}
