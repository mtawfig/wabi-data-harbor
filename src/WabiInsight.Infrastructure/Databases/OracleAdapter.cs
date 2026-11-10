using System.Diagnostics;
using Oracle.ManagedDataAccess.Client;
using WabiInsight.Core.Applications;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Organizations;

namespace WabiInsight.Infrastructure.Databases;

/// <summary>Oracle source adapter over the managed driver: dictionary views only (USER_*/ALL_*), never business rows. Live tests run only with WI_TEST_ORACLE_CONNECTION.</summary>
public sealed class OracleAdapter : IDatabaseAdapter
{
    public DatabaseType DatabaseType => DatabaseType.Oracle;

    public string Normalize(string connectionString)
    {
        OracleConnectionStringBuilder builder;
        try
        {
            builder = new OracleConnectionStringBuilder(connectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            throw new ArgumentException("connection_string_invalid", nameof(connectionString), ex);
        }

        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            throw new ArgumentException("connection_string_server_required", nameof(connectionString));
        }

        if (string.IsNullOrWhiteSpace(builder.UserID))
        {
            throw new ArgumentException("connection_string_user_required", nameof(connectionString));
        }

        // "User Id=/" is operating-system authentication: the database would see the account Wabi Insight runs under,
        // not a login the organization typed (P13.25).
        if (builder.UserID.Trim() == "/")
        {
            throw new ArgumentException("connection_string_server_identity_not_allowed", nameof(connectionString));
        }

        builder.ConnectionTimeout = Math.Clamp(builder.ConnectionTimeout is 0 or 15 ? 10 : builder.ConnectionTimeout, 3, 30);
        builder.Pooling = true;
        return builder.ConnectionString;
    }

    public string Hint(string connectionString)
    {
        var builder = new OracleConnectionStringBuilder(connectionString);
        return $"{builder.DataSource} / {builder.UserID}";
    }

    public async Task<ConnectionTestResult> TestAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        var warnings = new List<string>();
        try
        {
            await using var connection = new OracleConnection(Normalize(connectionString));
            await connection.OpenAsync(cancellationToken);
            var version = connection.ServerVersion;
            var user = await ScalarAsync(connection, "SELECT USER FROM DUAL", cancellationToken);
            var writes = await ScalarAsync(connection, """
                SELECT COUNT(*) FROM (
                  SELECT privilege FROM user_sys_privs WHERE privilege IN ('INSERT ANY TABLE','UPDATE ANY TABLE','DELETE ANY TABLE','CREATE ANY TABLE','ALTER ANY TABLE','DROP ANY TABLE')
                  UNION ALL SELECT granted_role FROM user_role_privs WHERE granted_role IN ('DBA')
                  UNION ALL SELECT privilege FROM user_tab_privs WHERE privilege IN ('INSERT','UPDATE','DELETE','ALTER'))
                """, cancellationToken);
            var readOnly = writes is not null && Convert.ToInt64(writes, System.Globalization.CultureInfo.InvariantCulture) == 0;
            if (!readOnly)
            {
                warnings.Add("account_not_read_only");
            }

            return new ConnectionTestResult(true, version, user?.ToString(), readOnly, warnings, null, watch.Elapsed.TotalMilliseconds);
        }
        catch (ArgumentException ex)
        {
            return new ConnectionTestResult(false, null, null, null, warnings, ex.Message, watch.Elapsed.TotalMilliseconds);
        }
        catch (OracleException ex)
        {
            return new ConnectionTestResult(false, null, null, null, warnings, $"ora_{ex.Number}", watch.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.Net.Sockets.SocketException)
        {
            return new ConnectionTestResult(false, null, null, null, warnings, "connection_failed", watch.Elapsed.TotalMilliseconds);
        }
    }

    public async Task<DiscoveredSchema> DiscoverAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new OracleConnection(Normalize(connectionString));
        await connection.OpenAsync(cancellationToken);
        var objects = new Dictionary<(string Schema, string Name), (DatabaseObjectType Type, List<DiscoveredColumn> Columns)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT o.owner, o.object_name, o.object_type, c.column_name, c.data_type, c.char_length, c.data_precision, c.data_scale, c.nullable, c.column_id,
                       CASE WHEN EXISTS (
                            SELECT 1 FROM all_constraints k JOIN all_cons_columns kc ON kc.owner = k.owner AND kc.constraint_name = k.constraint_name
                            WHERE k.constraint_type = 'P' AND k.owner = o.owner AND k.table_name = o.object_name AND kc.column_name = c.column_name) THEN 1 ELSE 0 END AS is_pk
                FROM all_objects o
                JOIN all_tab_columns c ON c.owner = o.owner AND c.table_name = o.object_name
                WHERE o.object_type IN ('TABLE','VIEW') AND o.owner = USER
                ORDER BY o.owner, o.object_name, c.column_id
                """;
            command.CommandTimeout = 60;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = (reader.GetString(0), reader.GetString(1));
                if (!objects.TryGetValue(key, out var entry))
                {
                    entry = (reader.GetString(2) == "VIEW" ? DatabaseObjectType.View : DatabaseObjectType.Table, []);
                    objects[key] = entry;
                }

                entry.Columns.Add(new DiscoveredColumn(
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : Convert.ToInt32(reader.GetValue(5), System.Globalization.CultureInfo.InvariantCulture),
                    reader.IsDBNull(6) ? null : Convert.ToInt32(reader.GetValue(6), System.Globalization.CultureInfo.InvariantCulture),
                    reader.IsDBNull(7) ? null : Convert.ToInt32(reader.GetValue(7), System.Globalization.CultureInfo.InvariantCulture),
                    reader.GetString(8) == "Y",
                    Convert.ToInt32(reader.GetValue(10), System.Globalization.CultureInfo.InvariantCulture) == 1,
                    Convert.ToInt32(reader.GetValue(9), System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        var relationships = new List<DiscoveredRelationship>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT k.constraint_name, k.owner, k.table_name, kc.column_name, r.owner, r.table_name, rc.column_name
                FROM all_constraints k
                JOIN all_cons_columns kc ON kc.owner = k.owner AND kc.constraint_name = k.constraint_name
                JOIN all_constraints r ON r.owner = k.r_owner AND r.constraint_name = k.r_constraint_name
                JOIN all_cons_columns rc ON rc.owner = r.owner AND rc.constraint_name = r.constraint_name AND rc.position = kc.position
                WHERE k.constraint_type = 'R' AND k.owner = USER
                ORDER BY k.constraint_name
                """;
            command.CommandTimeout = 60;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                relationships.Add(new DiscoveredRelationship(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6)));
            }
        }

        return new DiscoveredSchema(objects.Select(o => new DiscoveredObject(o.Key.Schema, o.Key.Name, o.Value.Type, o.Value.Columns)).ToList(), relationships);
    }

    private static async Task<object?> ScalarAsync(OracleConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 15;
        return await command.ExecuteScalarAsync(cancellationToken);
    }
}

public sealed class DatabaseAdapterFactory : IDatabaseAdapterFactory
{
    private readonly Dictionary<DatabaseType, IDatabaseAdapter> _adapters;

    public DatabaseAdapterFactory(IEnumerable<IDatabaseAdapter> adapters)
    {
        _adapters = adapters.ToDictionary(a => a.DatabaseType);
    }

    public IDatabaseAdapter Resolve(DatabaseType databaseType)
        => _adapters.TryGetValue(databaseType, out var adapter) ? adapter : throw new ArgumentException("database_type_unsupported", nameof(databaseType));
}
