using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Ai.Compilers;
using WabiInsight.Core.Applications;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Insights;
using WabiInsight.Core.Organizations;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Ai;

/// <summary>
/// Read-only execution (specification §19 step 12): the organization's own connection (decrypted for the
/// call), a statement timeout, a row cap, and a read-only transaction. SQL Server runs READ COMMITTED on
/// the read-only login the administrator registered; Oracle sets the transaction READ ONLY. The built-in
/// application runs on the insights login. The executor receives compiled SQL only, never model text.
/// </summary>
public sealed class QueryExecutor : IQueryExecutor
{
    private static readonly string[] NumericTypes = ["int", "bigint", "smallint", "tinyint", "decimal", "numeric", "money", "smallmoney", "float", "real", "number", "binary_double", "binary_float", "int32", "int64", "int16", "byte", "decimal", "double", "single"];

    private readonly ConfigurationDbContext _context;
    private readonly IConnectionStringProtector _protector;
    private readonly IDatabaseAdapterFactory _adapters;
    private readonly IInsightsConnectionFactory _insights;
    private readonly AiOptions _options;

    public QueryExecutor(ConfigurationDbContext context, IConnectionStringProtector protector, IDatabaseAdapterFactory adapters, IInsightsConnectionFactory insights, IOptions<AiOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _context = context;
        _protector = protector;
        _adapters = adapters;
        _insights = insights;
        _options = options.Value;
    }

    public async Task<AnalysisResult> ExecuteAsync(Guid applicationId, CompiledQuery query, PlanVisualization? visualization, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var application = await _context.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.ApplicationId == applicationId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "application_not_found");

        var watch = Stopwatch.StartNew();
        await using var connection = await OpenAsync(application, cancellationToken);
        DbTransaction? transaction = null;
        try
        {
            if (connection is OracleConnection)
            {
                transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                await using (var readOnly = connection.CreateCommand())
                {
                    readOnly.Transaction = transaction;
                    readOnly.CommandText = "SET TRANSACTION READ ONLY";
                    await readOnly.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = query.Sql;
            command.CommandTimeout = Math.Clamp(_options.QueryTimeoutSeconds, 5, 300);
            if (command is OracleCommand oracle)
            {
                oracle.BindByName = true;
            }

            foreach (var parameter in query.Parameters)
            {
                var p = command.CreateParameter();
                p.ParameterName = parameter.Name;
                p.Value = parameter.Value ?? DBNull.Value;
                command.Parameters.Add(p);
            }

            var columns = new List<ResultColumn>();
            var rows = new List<IReadOnlyList<object?>>();
            var truncated = false;
            await using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.SingleResult, cancellationToken))
            {
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var typeName = reader.GetDataTypeName(i).ToLowerInvariant();
                    var clr = reader.GetFieldType(i);
                    var numeric = NumericTypes.Any(t => typeName.StartsWith(t, StringComparison.Ordinal)) || clr == typeof(decimal) || clr == typeof(double) || clr == typeof(float) || clr == typeof(int) || clr == typeof(long) || clr == typeof(short);
                    var temporal = clr == typeof(DateTime) || clr == typeof(DateTimeOffset) || clr == typeof(DateOnly);
                    columns.Add(new ResultColumn(reader.GetName(i), typeName, numeric, temporal));
                }

                while (await reader.ReadAsync(cancellationToken))
                {
                    if (rows.Count >= query.RowCap)
                    {
                        truncated = true;
                        break;
                    }

                    var values = new object?[reader.FieldCount];
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        values[i] = await reader.IsDBNullAsync(i, cancellationToken) ? null : Normalize(reader.GetValue(i));
                    }

                    rows.Add(values);
                }
            }

            var (kpis, series) = ResultCalculator.Calculate(columns, rows, visualization);
            return new AnalysisResult(columns, rows, rows.Count, truncated, kpis, series, watch.Elapsed.TotalMilliseconds);
        }
        catch (DbException ex)
        {
            throw new AnalysisExecutionException(MapError(ex), ex.Message);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task<DbConnection> OpenAsync(Application application, CancellationToken cancellationToken)
    {
        if (application.DatabaseType == DatabaseType.PlatformInsights)
        {
            return await _insights.OpenAsync(cancellationToken);
        }

        if (string.IsNullOrEmpty(application.EncryptedConnectionString))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "connection_not_configured");
        }

        var connectionString = _adapters.Resolve(application.DatabaseType).Normalize(_protector.Unprotect(application.EncryptedConnectionString));
        DbConnection connection = application.DatabaseType switch
        {
            DatabaseType.SqlServer => new SqlConnection(new SqlConnectionStringBuilder(connectionString) { ApplicationIntent = ApplicationIntent.ReadOnly }.ConnectionString),
            DatabaseType.Oracle => new OracleConnection(connectionString),
            _ => throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "database_type_unsupported"),
        };
        try
        {
            await connection.OpenAsync(cancellationToken);
            if (connection is SqlConnection sql)
            {
                await using var isolation = sql.CreateCommand();
                isolation.CommandText = "SET TRANSACTION ISOLATION LEVEL READ COMMITTED";
                await isolation.ExecuteNonQueryAsync(cancellationToken);
            }

            return connection;
        }
        catch (DbException ex)
        {
            await connection.DisposeAsync();
            throw new AnalysisExecutionException("source_unreachable", ex.Message);
        }
    }

    private static object? Normalize(object value) => value switch
    {
        byte[] => "<binary>",
        _ => value,
    };

    private static string MapError(DbException ex) => ex switch
    {
        SqlException s when s.Number == -2 => "query_timeout",
        SqlException s when s.Number is 229 or 230 => "source_permission_denied",
        SqlException s when s.Number is 207 or 208 => "source_schema_changed",
        OracleException o when o.Number == 1013 => "query_timeout",
        OracleException o when o.Number is 942 or 904 => "source_schema_changed",
        OracleException o when o.Number == 1031 => "source_permission_denied",
        _ => "query_failed",
    };
}

/// <summary>Execution failure with a stable code; the raw database message stays in the server log.</summary>
public sealed class AnalysisExecutionException : Exception
{
    public AnalysisExecutionException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public AnalysisExecutionException()
    {
        Code = "query_failed";
    }

    public AnalysisExecutionException(string message)
        : base(message)
    {
        Code = "query_failed";
    }

    public AnalysisExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "query_failed";
    }

    public string Code { get; }
}
