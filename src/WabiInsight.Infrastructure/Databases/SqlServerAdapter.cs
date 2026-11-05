using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using WabiInsight.Core.Applications;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Organizations;

namespace WabiInsight.Infrastructure.Databases;

/// <summary>SQL Server source adapter: catalog views only (sys.*), read-only intent, short timeouts, never business rows.</summary>
public sealed class SqlServerAdapter : IDatabaseAdapter
{
    public DatabaseType DatabaseType => DatabaseType.SqlServer;

    public string Normalize(string connectionString)
    {
        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException)
        {
            throw new ArgumentException("connection_string_invalid", nameof(connectionString), ex);
        }

        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            throw new ArgumentException("connection_string_server_required", nameof(connectionString));
        }

        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
        {
            throw new ArgumentException("connection_string_database_required", nameof(connectionString));
        }

        builder.ConnectTimeout = Math.Clamp(builder.ConnectTimeout is 0 or 15 ? 10 : builder.ConnectTimeout, 3, 30);
        builder.ApplicationName = "WabiInsight";
        builder.ApplicationIntent = ApplicationIntent.ReadOnly;
        builder.MultipleActiveResultSets = false;
        builder.Pooling = true;
        builder.DataSource = builder.DataSource.Trim();
        builder.InitialCatalog = builder.InitialCatalog.Trim();
        return builder.ConnectionString;
    }

    public string Hint(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        return $"{builder.DataSource} / {builder.InitialCatalog}";
    }

    public async Task<ConnectionTestResult> TestAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        var warnings = new List<string>();
        try
        {
            await using var connection = new SqlConnection(Normalize(connectionString));
            await connection.OpenAsync(cancellationToken);
            var version = connection.ServerVersion;
            var database = connection.Database;

            // Read-only verification: server roles, database roles and explicit write permissions of the connected login.
            var writePermissions = await ScalarAsync<int>(connection, """
                SELECT COUNT(*) FROM fn_my_permissions(NULL, 'DATABASE')
                WHERE permission_name IN ('INSERT','UPDATE','DELETE','ALTER','CONTROL','ALTER ANY SCHEMA','CREATE TABLE')
                """, cancellationToken);
            var elevated = await ScalarAsync<int>(connection, """
                SELECT CASE WHEN IS_SRVROLEMEMBER('sysadmin') = 1 OR IS_MEMBER('db_owner') = 1 OR IS_MEMBER('db_datawriter') = 1 OR IS_MEMBER('db_ddladmin') = 1 THEN 1 ELSE 0 END
                """, cancellationToken);
            var readOnly = writePermissions == 0 && elevated == 0;
            if (!readOnly)
            {
                warnings.Add("account_not_read_only");
            }

            return new ConnectionTestResult(true, version, database, readOnly, warnings, null, watch.Elapsed.TotalMilliseconds);
        }
        catch (ArgumentException ex)
        {
            return new ConnectionTestResult(false, null, null, null, warnings, ex.Message, watch.Elapsed.TotalMilliseconds);
        }
        catch (SqlException ex)
        {
            return new ConnectionTestResult(false, null, null, null, warnings, MapError(ex.Number), watch.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.Net.Sockets.SocketException)
        {
            return new ConnectionTestResult(false, null, null, null, warnings, "connection_failed", watch.Elapsed.TotalMilliseconds);
        }
    }

    public async Task<DiscoveredSchema> DiscoverAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(Normalize(connectionString));
        await connection.OpenAsync(cancellationToken);

        var objects = new Dictionary<(string Schema, string Name), (DatabaseObjectType Type, List<DiscoveredColumn> Columns)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT s.name AS SchemaName, o.name AS ObjectName, o.type AS ObjectType,
                       c.name AS ColumnName, t.name AS DataType, c.max_length, c.precision, c.scale, c.is_nullable, c.column_id,
                       CASE WHEN EXISTS (
                            SELECT 1 FROM sys.index_columns ic
                            JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                            WHERE i.is_primary_key = 1 AND ic.object_id = c.object_id AND ic.column_id = c.column_id) THEN 1 ELSE 0 END AS IsPrimaryKey
                FROM sys.objects o
                JOIN sys.schemas s ON s.schema_id = o.schema_id
                JOIN sys.columns c ON c.object_id = o.object_id
                JOIN sys.types t ON t.user_type_id = c.user_type_id
                WHERE o.type IN ('U','V') AND o.is_ms_shipped = 0 AND s.name NOT IN ('sys','INFORMATION_SCHEMA')
                ORDER BY s.name, o.name, c.column_id
                """;
            command.CommandTimeout = 60;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = (reader.GetString(0), reader.GetString(1));
                if (!objects.TryGetValue(key, out var entry))
                {
                    entry = (reader.GetString(2).Trim() == "V" ? DatabaseObjectType.View : DatabaseObjectType.Table, []);
                    objects[key] = entry;
                }

                var maxLength = reader.GetInt16(5);
                entry.Columns.Add(new DiscoveredColumn(
                    reader.GetString(3),
                    reader.GetString(4),
                    maxLength < 0 ? null : maxLength,
                    reader.GetByte(6),
                    reader.GetByte(7),
                    reader.GetBoolean(8),
                    reader.GetInt32(10) == 1,
                    reader.GetInt32(9)));
            }
        }

        var relationships = new List<DiscoveredRelationship>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT fk.name, ps.name, pt.name, pc.name, rs.name, rt.name, rc.name
                FROM sys.foreign_key_columns fkc
                JOIN sys.foreign_keys fk ON fk.object_id = fkc.constraint_object_id
                JOIN sys.tables pt ON pt.object_id = fkc.parent_object_id
                JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
                JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
                JOIN sys.tables rt ON rt.object_id = fkc.referenced_object_id
                JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
                JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
                ORDER BY fk.name
                """;
            command.CommandTimeout = 60;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                relationships.Add(new DiscoveredRelationship(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6)));
            }
        }

        return new DiscoveredSchema(
            objects.Select(o => new DiscoveredObject(o.Key.Schema, o.Key.Name, o.Value.Type, o.Value.Columns)).OrderBy(o => o.SchemaName).ThenBy(o => o.ObjectName).ToList(),
            relationships);
    }

    /// <summary>Stable, non-revealing codes for the common failures; the raw number stays in the server log only.</summary>
    internal static string MapError(int number) => number switch
    {
        4060 => "database_not_found",
        18456 or 18452 => "login_failed",
        -1 or 53 or 10061 or 11001 or 258 => "server_unreachable",
        -2 => "connection_timeout",
        229 or 230 or 297 => "permission_denied",
        _ => $"sql_error_{number}",
    };

    private static async Task<T> ScalarAsync<T>(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 15;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return (T)Convert.ChangeType(value ?? default(T)!, typeof(T), System.Globalization.CultureInfo.InvariantCulture)!;
    }
}
