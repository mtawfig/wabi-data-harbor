using System.Data.Common;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Applications;
using WabiInsight.Core.Insights;
using WabiInsight.Infrastructure.Databases;
using WabiInsight.Infrastructure.DependencyInjection;

namespace WabiInsight.Infrastructure.Insights;

public sealed class InsightsOptions
{
    public const string SectionName = "Insights";

    /// <summary>Connection string of the dedicated login that has SELECT on the insights schema only (scripts/sql/create-insights-login.sql). Secret: keep it out of appsettings.json.</summary>
    public string? ConnectionString { get; set; }

    public int CommandTimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Opens insights connections with the dedicated read-only login. Without one configured (local development)
/// it falls back to the configuration-database connection and says so in the source status, so the gap is
/// visible on the Platform Insights Configuration page rather than silent.
/// </summary>
public sealed partial class InsightsConnectionFactory : IInsightsConnectionFactory
{
    private readonly string _connectionString;
    private readonly ILogger<InsightsConnectionFactory> _logger;
    private bool _warned;

    public InsightsConnectionFactory(IOptions<InsightsOptions> options, IConfiguration configuration, ILogger<InsightsConnectionFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);
        _logger = logger;
        var dedicated = options.Value.ConnectionString;
        DedicatedLoginConfigured = !string.IsNullOrWhiteSpace(dedicated);
        var raw = DedicatedLoginConfigured ? dedicated! : configuration.GetConnectionString(ServiceCollectionExtensions.ConfigurationDatabaseConnectionName) ?? string.Empty;
        _connectionString = new SqlConnectionStringBuilder(raw) { ApplicationIntent = ApplicationIntent.ReadOnly, ApplicationName = "WabiInsight-Insights", ConnectTimeout = 10 }.ConnectionString;
        CommandTimeoutSeconds = options.Value.CommandTimeoutSeconds;
    }

    public bool DedicatedLoginConfigured { get; }

    public int CommandTimeoutSeconds { get; }

    public async Task<DbConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        if (!DedicatedLoginConfigured && !_warned)
        {
            _warned = true;
            LogFallback(_logger);
        }

        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task<ConnectionTestResult> TestAsync(CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        var warnings = new List<string>();
        try
        {
            await using var connection = (SqlConnection)await OpenAsync(cancellationToken);
            var confined = await IsConfinedAsync(connection, cancellationToken);
            if (!confined)
            {
                warnings.Add(DedicatedLoginConfigured ? "insights_login_not_confined" : "insights_login_not_configured");
            }

            if (!await CanReadViewsAsync(connection, cancellationToken))
            {
                warnings.Add("insights_views_not_readable");
            }

            return new ConnectionTestResult(true, connection.ServerVersion, connection.Database, confined, warnings, null, watch.Elapsed.TotalMilliseconds);
        }
        catch (SqlException ex)
        {
            return new ConnectionTestResult(false, null, null, null, warnings, SqlServerAdapter.MapError(ex.Number), watch.Elapsed.TotalMilliseconds);
        }
        catch (InvalidOperationException)
        {
            return new ConnectionTestResult(false, null, null, null, warnings, "connection_failed", watch.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>True when the login can read the insights schema but not the raw dbo tables.</summary>
    private static async Task<bool> IsConfinedAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CASE WHEN HAS_PERMS_BY_NAME('dbo.OrganizationUsers', 'OBJECT', 'SELECT') = 0
                         AND HAS_PERMS_BY_NAME('dbo.AuditLogs', 'OBJECT', 'SELECT') = 0
                         AND IS_SRVROLEMEMBER('sysadmin') = 0 AND IS_MEMBER('db_owner') = 0 AND IS_MEMBER('db_datareader') = 0
                    THEN 1 ELSE 0 END
            """;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is int i && i == 1;
    }

    /// <summary>
    /// True when every insights view can actually be read. Seeing the views is not enough: when the schema and the
    /// tables have different owners, SQL Server lists the views to this login and then refuses to read them (P13.43).
    /// </summary>
    private async Task<bool> CanReadViewsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var views = new List<string>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT QUOTENAME(name) FROM sys.views WHERE schema_id = SCHEMA_ID(N'insights') ORDER BY name";
            await using var reader = await list.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                views.Add(reader.GetString(0));
            }
        }

        foreach (var view in views)
        {
            try
            {
                await using var probe = connection.CreateCommand();
                probe.CommandText = $"SELECT TOP (0) * FROM [insights].{view}";   // the name comes from sys.views through QUOTENAME
                probe.CommandTimeout = CommandTimeoutSeconds;
                await probe.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqlException ex)
            {
                LogViewNotReadable(_logger, view, ex.Number, ex.Message);
                return false;
            }
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The insights login cannot read the view [insights].{View} (SQL error {Number}: {Reason}). If the reason is a permission on a dbo table, apply the database migrations: AlignInsightsSchemaOwner gives the insights schema the owner of dbo.")]
    private static partial void LogViewNotReadable(ILogger logger, string view, int number, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Insights:ConnectionString is not configured; insights queries use the configuration-database login. Configure the dedicated read-only login before production (scripts/sql/create-insights-login.sql).")]
    private static partial void LogFallback(ILogger logger);
}
