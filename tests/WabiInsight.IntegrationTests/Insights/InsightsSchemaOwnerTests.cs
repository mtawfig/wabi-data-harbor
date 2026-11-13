using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WabiInsight.Infrastructure.Insights;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Insights;

/// <summary>
/// The insights views must be readable by a login that has SELECT on the <c>insights</c> schema only (P13.43).
/// On a server where the application's login is a member of db_owner (the deployment guides), not the database
/// owner itself, the schema used to belong to that login: SQL Server then refused the insights login on every view
/// and Organization Insights answered "insights query failed". The migration AlignInsightsSchemaOwner corrects it.
/// </summary>
public sealed class InsightsSchemaOwnerTests : IClassFixture<EmptySqlServerTestDatabase>, IAsyncLifetime
{
    private const string Password = "Wi_T3st!_Schema_Owner_2026";
    private readonly EmptySqlServerTestDatabase _db;
    private readonly string _owner = $"wi_t_owner_{Guid.NewGuid():N}";
    private readonly string _reader = $"wi_t_reader_{Guid.NewGuid():N}";

    public InsightsSchemaOwnerTests(EmptySqlServerTestDatabase db)
    {
        _db = db;
    }

    public async Task InitializeAsync()
    {
        // The state a guide-built server was in: the schema belongs to the application's login (a db_owner member),
        // and the insights login has SELECT on the schema and nothing on dbo (scripts/sql/create-insights-login.sql).
        await ExecuteAsync(_db.ConnectionString, $"""
            CREATE LOGIN [{_owner}] WITH PASSWORD = N'{Password}', CHECK_POLICY = OFF;
            CREATE LOGIN [{_reader}] WITH PASSWORD = N'{Password}', CHECK_POLICY = OFF;
            CREATE USER [{_owner}] FOR LOGIN [{_owner}];
            ALTER ROLE db_owner ADD MEMBER [{_owner}];
            CREATE USER [{_reader}] FOR LOGIN [{_reader}];
            ALTER AUTHORIZATION ON SCHEMA::[insights] TO [{_owner}];
            GRANT SELECT ON SCHEMA::[insights] TO [{_reader}];
            DENY SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::[dbo] TO [{_reader}];
            """);
    }

    public async Task DisposeAsync()
    {
        // Logins belong to the server, not to the test database: remove them (the fixture drops the database).
        await ExecuteAsync(_db.ConnectionString, $"""
            IF SCHEMA_ID(N'insights') IS NOT NULL ALTER AUTHORIZATION ON SCHEMA::[insights] TO [dbo];
            IF DATABASE_PRINCIPAL_ID(N'{_owner}') IS NOT NULL DROP USER [{_owner}];
            IF DATABASE_PRINCIPAL_ID(N'{_reader}') IS NOT NULL DROP USER [{_reader}];
            """);
        await ExecuteAsync(As("master", null), $"""
            IF SUSER_ID(N'{_owner}') IS NOT NULL DROP LOGIN [{_owner}];
            IF SUSER_ID(N'{_reader}') IS NOT NULL DROP LOGIN [{_reader}];
            """, pooling: false);
    }

    private string As(string? database, string? login) => new SqlConnectionStringBuilder(_db.ConnectionString)
    {
        InitialCatalog = database ?? _db.DatabaseName,
        UserID = login ?? new SqlConnectionStringBuilder(_db.ConnectionString).UserID,
        Password = login is null ? new SqlConnectionStringBuilder(_db.ConnectionString).Password : Password,
        Pooling = false,   // these logins are dropped at the end
    }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql, bool pooling = true)
    {
        await using var connection = new SqlConnection(pooling ? connectionString : new SqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private InsightsConnectionFactory Factory() => new(
        Options.Create(new InsightsOptions { ConnectionString = As(null, _reader) }),
        new ConfigurationBuilder().Build(),
        NullLogger<InsightsConnectionFactory>.Instance);

    [Fact]
    public async Task InsightsLogin_ReadsEveryView_OnceTheSchemaHasTheOwnerOfDbo_AndKeepsItsGrants()
    {
        var reader = As(null, _reader);

        // Before: the views are listed to the insights login, but it is refused on the tables they read.
        var refused = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(reader, "SELECT TOP (0) * FROM [insights].[Users]"));
        Assert.Equal(229, refused.Number);
        var before = await Factory().TestAsync();
        Assert.True(before.Succeeded);
        Assert.Contains("insights_views_not_readable", before.Warnings);

        // The migration's statement, run by the application's login (a member of db_owner, as on a guide-built server).
        await ExecuteAsync(As(null, _owner), InsightsSchema.AlignOwnerSql);

        Assert.Equal("dbo", await ScalarAsync(_db.ConnectionString, "SELECT USER_NAME(principal_id) FROM sys.schemas WHERE name = N'insights'"));
        foreach (var (view, _) in InsightsSchema.Views)
        {
            await ExecuteAsync(reader, $"SELECT TOP (0) * FROM [insights].[{view}]");
        }

        // The grant on the schema survived the change of owner, and the login is still kept out of the tables.
        Assert.Equal(1, await ScalarAsync(_db.ConnectionString, $"SELECT COUNT(*) FROM sys.database_permissions WHERE class = 3 AND major_id = SCHEMA_ID(N'insights') AND grantee_principal_id = DATABASE_PRINCIPAL_ID(N'{_reader}') AND permission_name = N'SELECT' AND state = 'G'"));
        var stillRefused = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(reader, "SELECT TOP (0) * FROM [dbo].[OrganizationUsers]"));
        Assert.Equal(229, stillRefused.Number);
        var after = await Factory().TestAsync();
        Assert.True(after.Succeeded);
        Assert.True(after.ReadOnlyVerified);
        Assert.DoesNotContain("insights_views_not_readable", after.Warnings);

        // Running it again changes nothing.
        await ExecuteAsync(As(null, _owner), InsightsSchema.AlignOwnerSql);
        Assert.Equal("dbo", await ScalarAsync(_db.ConnectionString, "SELECT USER_NAME(principal_id) FROM sys.schemas WHERE name = N'insights'"));
        await ExecuteAsync(reader, "SELECT TOP (0) * FROM [insights].[Users]");
    }
}
