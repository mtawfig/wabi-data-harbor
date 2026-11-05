using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using WabiInsight.Core.Common;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.IntegrationTests.Database;

/// <summary>
/// One real SQL Server database per test run, migrated from empty and seeded with two organizations.
/// Resolution order: WI_TEST_SQLSERVER_CONNECTION (CI service container / local server),
/// then a Testcontainers SQL Server 2022 container when Docker is available. Never SQLite or in-memory:
/// constraints, rowversion and filtered indexes must be verified on the real engine.
/// </summary>
public class SqlServerTestDatabase : IAsyncLifetime
{
    public const string ConnectionVariable = "WI_TEST_SQLSERVER_CONNECTION";

    private readonly bool _seed;
    private MsSqlContainer? _container;
    private string _serverConnectionString = string.Empty;

    public SqlServerTestDatabase()
        : this(seed: true)
    {
    }

    protected SqlServerTestDatabase(bool seed)
    {
        _seed = seed;
    }

    public string DatabaseName { get; } = $"WabiInsightTests_{Guid.NewGuid():N}";

    public string ConnectionString { get; private set; } = string.Empty;

    public TestData Data { get; } = new();

    public async Task InitializeAsync()
    {
        _serverConnectionString = await ResolveServerConnectionStringAsync();
        ConnectionString = new SqlConnectionStringBuilder(_serverConnectionString)
        {
            InitialCatalog = DatabaseName,
        }.ConnectionString;

        await using (var context = CreateContext(TestData.SystemPrincipal))
        {
            await context.Database.MigrateAsync();
        }

        if (_seed)
        {
            await Data.SeedAsync(this);
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            await using var connection = new SqlConnection(new SqlConnectionStringBuilder(_serverConnectionString)
            {
                InitialCatalog = "master",
            }.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"IF DB_ID('{DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}]; END";
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            if (_container is not null)
            {
                await _container.DisposeAsync();
            }
        }
    }

    /// <summary>Creates a DbContext whose tenant context is initialized with the given principal.</summary>
    public ConfigurationDbContext CreateContext(TenantPrincipal principal)
    {
        var options = new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.MigrationsAssembly(typeof(ConfigurationDbContext).Assembly.FullName))
            .EnableSensitiveDataLogging()
            .Options;

        var tenantContext = new TenantContext();
        if (principal.PrincipalType != PrincipalType.Anonymous)
        {
            tenantContext.Initialize(principal);
        }

        return new ConfigurationDbContext(options, tenantContext, new SystemClock());
    }

    private async Task<string> ResolveServerConnectionStringAsync()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        try
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            await _container.StartAsync();
            return _container.GetConnectionString();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Integration tests need SQL Server. Set {ConnectionVariable} to a server connection string " +
                "(for example the docker-compose.dev.yml instance) or make Docker available for Testcontainers.", ex);
        }
    }
}

/// <summary>A migrated but unseeded database for bootstrap scenarios.</summary>
public sealed class EmptySqlServerTestDatabase : SqlServerTestDatabase
{
    public EmptySqlServerTestDatabase()
        : base(seed: false)
    {
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollectionDefinition : ICollectionFixture<SqlServerTestDatabase>
{
    public const string Name = "database";
}

[CollectionDefinition(Name)]
public sealed class EmptyDatabaseCollectionDefinition : ICollectionFixture<EmptySqlServerTestDatabase>
{
    public const string Name = "empty-database";
}
