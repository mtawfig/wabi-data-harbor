using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Organizations;

namespace WabiInsight.Core.Applications;

public sealed record DiscoveredColumn(string ColumnName, string DataType, int? MaxLength, int? Precision, int? Scale, bool IsNullable, bool IsPrimaryKey, int OrdinalPosition);

public sealed record DiscoveredObject(string SchemaName, string ObjectName, DatabaseObjectType ObjectType, IReadOnlyList<DiscoveredColumn> Columns);

public sealed record DiscoveredRelationship(string ConstraintName, string FromSchema, string FromObject, string FromColumn, string ToSchema, string ToObject, string ToColumn);

public sealed record DiscoveredSchema(IReadOnlyList<DiscoveredObject> Objects, IReadOnlyList<DiscoveredRelationship> Relationships);

/// <summary>
/// Dialect adapter for a source engine. Opens connections only with the organization's own connection string,
/// reads catalog views only (never business rows) and never executes model-generated SQL.
/// </summary>
public interface IDatabaseAdapter
{
    DatabaseType DatabaseType { get; }

    /// <summary>Normalizes and validates a connection string; throws <see cref="ArgumentException"/> for unusable input. Secrets stay in memory.</summary>
    string Normalize(string connectionString);

    /// <summary>Server/database hint derived from the connection string for display (no credentials).</summary>
    string Hint(string connectionString);

    Task<ConnectionTestResult> TestAsync(string connectionString, CancellationToken cancellationToken = default);

    Task<DiscoveredSchema> DiscoverAsync(string connectionString, CancellationToken cancellationToken = default);
}

public interface IDatabaseAdapterFactory
{
    IDatabaseAdapter Resolve(DatabaseType databaseType);
}
