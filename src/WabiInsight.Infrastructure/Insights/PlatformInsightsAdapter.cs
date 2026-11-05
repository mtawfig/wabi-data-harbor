using Microsoft.Data.SqlClient;
using WabiInsight.Core.Applications;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Insights;

namespace WabiInsight.Infrastructure.Insights;

/// <summary>
/// Adapter of the built-in application. It has no organization connection string: every organization reads
/// the same insights views through the platform's read-only login, scoped by the mandatory predicate.
/// </summary>
public sealed class PlatformInsightsAdapter : IDatabaseAdapter
{
    private readonly IInsightsConnectionFactory _connections;

    public PlatformInsightsAdapter(IInsightsConnectionFactory connections)
    {
        _connections = connections;
    }

    public DatabaseType DatabaseType => DatabaseType.PlatformInsights;

    public string Normalize(string connectionString) => throw new ArgumentException("platform_insights_has_no_connection_string", nameof(connectionString));

    public string Hint(string connectionString) => "insights schema (configuration database)";

    public Task<ConnectionTestResult> TestAsync(string connectionString, CancellationToken cancellationToken = default) => _connections.TestAsync(cancellationToken);

    public async Task<DiscoveredSchema> DiscoverAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = (SqlConnection)await _connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE, IS_NULLABLE, ORDINAL_POSITION
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME IN (SELECT TABLE_NAME FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_SCHEMA = @schema)
            ORDER BY TABLE_NAME, ORDINAL_POSITION
            """;
        command.Parameters.AddWithValue("@schema", InsightsSchema.SchemaName);
        var objects = new Dictionary<string, List<DiscoveredColumn>>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var view = reader.GetString(0);
            if (!objects.TryGetValue(view, out var columns))
            {
                columns = [];
                objects[view] = columns;
            }

            columns.Add(new DiscoveredColumn(
                reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4), System.Globalization.CultureInfo.InvariantCulture),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                string.Equals(reader.GetString(6), "YES", StringComparison.OrdinalIgnoreCase), false, reader.GetInt32(7)));
        }

        return new DiscoveredSchema(objects.Select(o => new DiscoveredObject(InsightsSchema.SchemaName, o.Key, DatabaseObjectType.View, o.Value)).ToList(), []);
    }
}
