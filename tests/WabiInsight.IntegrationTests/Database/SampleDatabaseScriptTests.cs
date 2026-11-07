using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using WabiInsight.Core.Security;
using WabiInsight.Infrastructure.Security;

namespace WabiInsight.IntegrationTests.Database;

/// <summary>
/// Runs scripts/sql/samples 01–04 exactly as an operator would (batches split on GO, one connection per script)
/// against a freshly migrated configuration database that already holds the two test organizations.
/// Database names get a per-run suffix so parallel runs on a shared CI server never collide.
/// </summary>
public sealed class SampleDatabaseScriptTests : IClassFixture<SampleDatabaseScriptTests.Fixture>
{
    private const string InitialPassword = "Wabi#Sample#2026";
    private readonly Fixture _fixture;

    public SampleDatabaseScriptTests(Fixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SourceScripts_CreateTheThreeDatabasesWithData()
    {
        Assert.True(await _fixture.ScalarAsync<int>($"SELECT COUNT(*) FROM [{_fixture.FlightDb}].dbo.Flights") > 100_000);
        Assert.Equal(599, await _fixture.ScalarAsync<int>($"SELECT COUNT(*) FROM [{_fixture.HrDb}].dbo.Employees"));
        Assert.True(await _fixture.ScalarAsync<int>($"SELECT COUNT(*) FROM [{_fixture.HrDb}].dbo.AttendanceRecords") > 100_000);
        Assert.True(await _fixture.ScalarAsync<int>($"SELECT COUNT(*) FROM [{_fixture.ProcDb}].dbo.PurchaseOrders") > 1_000);
        Assert.Equal(0, await _fixture.ScalarAsync<int>($"SELECT COUNT(*) FROM [{_fixture.ProcDb}].dbo.StockBalances WHERE QuantityOnHand < 0"));
        Assert.Equal(1, await _fixture.ScalarAsync<int>("SELECT COUNT(*) FROM sys.server_principals WHERE name = N'hr_reader'"));
    }

    [Fact]
    public async Task Seed_CreatesOrganizationWithFourUsersInTheirRoles()
    {
        var users = await _fixture.RowsAsync(
            """
            SELECT ou.EmployeeNumber, r.RoleCode, d.DepartmentCode, ou.MustChangePassword, ou.PasswordHash, li.PrincipalId
            FROM dbo.OrganizationUsers ou
            JOIN dbo.Organizations o ON o.OrganizationId = ou.OrganizationId AND o.OrganizationCode = N'GAS'
            JOIN dbo.UserRoles ur ON ur.UserId = ou.UserId JOIN dbo.Roles r ON r.RoleId = ur.RoleId
            JOIN dbo.Departments d ON d.DepartmentId = ou.DepartmentId
            JOIN dbo.LoginIdentities li ON li.NormalizedEmail = ou.NormalizedEmail AND li.OrganizationId = ou.OrganizationId
            ORDER BY ou.EmployeeNumber, r.RoleCode
            """);

        Assert.Equal(
            ["E1001:CyberSecurity:IT", "E1001:OrganizationAdmin:IT", "E1002:Management:EXEC", "E1003:OperationsAnalyst:OPS", "E1004:HRPayrollOfficer:HR"],
            users.Select(u => $"{u[0]}:{u[1]}:{u[2]}"));
        var hasher = new IdentityPasswordHashingService();
        Assert.All(users, u =>
        {
            Assert.True((bool)u[3], "Sample users must change the initial password at first sign-in.");
            Assert.Equal(PasswordVerification.Success, hasher.Verify((string)u[4], InitialPassword));
        });
    }

    [Fact]
    public async Task Seed_CataloguesEveryTableAndMatchesTheSourceSchema()
    {
        foreach (var (code, db) in new[] { ("FLIGHT-OPS", _fixture.FlightDb), ("HR-PAYROLL", _fixture.HrDb), ("PROCUREMENT", _fixture.ProcDb) })
        {
            var sourceColumns = await _fixture.ScalarAsync<int>(
                $"SELECT COUNT(*) FROM [{db}].sys.columns c JOIN [{db}].sys.objects o ON o.object_id = c.object_id WHERE o.type IN ('U','V') AND o.is_ms_shipped = 0");
            var catalogued = await _fixture.ScalarAsync<int>(
                $"""
                SELECT COUNT(*) FROM dbo.DatabaseColumns c JOIN dbo.DatabaseObjects o ON o.ObjectId = c.ObjectId
                JOIN dbo.Applications a ON a.ApplicationId = o.ApplicationId WHERE a.ApplicationCode = N'{code}' AND c.IsApproved = 1 AND o.IsApproved = 1
                """);
            Assert.Equal(sourceColumns, catalogued);
            Assert.Equal(0, await _fixture.ScalarAsync<int>(
                $"SELECT COUNT(*) FROM dbo.DatabaseObjects o JOIN dbo.Applications a ON a.ApplicationId = o.ApplicationId WHERE a.ApplicationCode = N'{code}' AND o.DisplayNameEn IS NULL"));
        }

        // Connection strings are encrypted by the application's key ring, so the seed leaves them for the administrator.
        Assert.Equal(3, await _fixture.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Applications WHERE IsBuiltIn = 0 AND EncryptedConnectionString IS NULL AND IsApproved = 0"));
    }

    [Fact]
    public async Task Seed_GrantsRoleScopedPermissions()
    {
        // The HR officer never receives a Restricted column (Emirates ID, passport, IBAN).
        Assert.Equal(0, await _fixture.ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM dbo.RoleTablePermissions p JOIN dbo.Roles r ON r.RoleId = p.RoleId AND r.RoleCode = N'HRPayrollOfficer'
            JOIN dbo.DatabaseColumns c ON c.ObjectId = p.ObjectId AND c.Classification = N'Restricted'
            WHERE p.AllColumns = 1 OR EXISTS (SELECT 1 FROM dbo.RoleColumnPermissions cp WHERE cp.RoleId = p.RoleId AND cp.ColumnId = c.ColumnId)
            """));
        Assert.Equal(3, await _fixture.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.DatabaseColumns WHERE Classification = N'Restricted'"));

        var scopes = await _fixture.RowsAsync(
            """
            SELECT r.RoleCode, o.ObjectName, rp.Scope FROM dbo.RoleRowPermissions rp
            JOIN dbo.Roles r ON r.RoleId = rp.RoleId JOIN dbo.DatabaseObjects o ON o.ObjectId = rp.ObjectId
            WHERE o.ObjectName IN (N'Payslips', N'PurchaseRequisitions', N'Flights')
            ORDER BY r.RoleCode, o.ObjectName
            """);
        Assert.Equal(
            ["HRPayrollOfficer:Payslips:All", "HRPayrollOfficer:PurchaseRequisitions:Department", "OperationsAnalyst:Flights:All", "OperationsAnalyst:Payslips:Self", "OperationsAnalyst:PurchaseRequisitions:All"],
            scopes.Select(s => $"{s[0]}:{s[1]}:{s[2]}"));

        // Every scoped grant has the mapping column it needs.
        Assert.Equal(0, await _fixture.ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM dbo.RoleRowPermissions rp LEFT JOIN dbo.RowScopeColumnMappings m ON m.ObjectId = rp.ObjectId
            WHERE (rp.Scope = N'Self' AND m.EmployeeColumnId IS NULL) OR (rp.Scope = N'Department' AND m.DepartmentColumnId IS NULL)
            """));
        Assert.Equal(0, await _fixture.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.RoleTablePermissions p JOIN dbo.Roles r ON r.RoleId = p.RoleId WHERE r.RoleCode IN (N'OrganizationAdmin', N'Management')"));
    }

    [Fact]
    public async Task Seed_AddsTheOwnersMonitoringExample_WithFieldRoles_AndThePlantedSignInsAreFound()
    {
        // P14.2: the same portal account used from several devices; the live view always ends today.
        var rule = await _fixture.RowsAsync(
            """
            SELECT mr.DefinitionJson, mr.Kind, mr.Severity, mr.TemplateCode, owner.EmployeeNumber, r.RoleCode
            FROM dbo.MonitoringRules mr
            JOIN dbo.OrganizationUsers owner ON owner.UserId = mr.OwnerUserId
            JOIN dbo.MonitoringRuleRecipients rr ON rr.RuleId = mr.RuleId JOIN dbo.Roles r ON r.RoleId = rr.RoleId
            WHERE mr.Name = N'Same account, several devices (employee portal)'
            """);
        var row = Assert.Single(rule);
        Assert.Equal(("Data", "High", "multi_device_same_account", "E1002", "CyberSecurity"), ((string)row[1], (string)row[2], (string)row[3], (string)row[4], (string)row[5]));
        var definition = Core.Monitoring.RuleDefinition.Parse((string)row[0]);
        Assert.Empty(Core.Monitoring.RuleDefinitionValidator.Validate(Core.Entities.Monitoring.MonitoringRuleKind.Data, definition));
        Assert.Equal(definition.Data!.ObjectId, await _fixture.ScalarAsync<Guid>("SELECT ObjectId FROM dbo.DatabaseObjects WHERE ObjectName = N'vw_EmployeePortalSignInsLive'"));
        Assert.Equal(3, await _fixture.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.DatabaseColumns WHERE ColumnId IN ('{definition.Data.TimeColumnId}', '{definition.Data.GroupByColumnIds[0]}', '{definition.Data.Measure.ColumnId}') AND SemanticRole IN (3, 4, 5)"));

        var planted = await _fixture.ScalarAsync<int>(
            $"""
            SELECT COUNT(*) FROM (SELECT AccountName FROM [{_fixture.HrDb}].dbo.vw_EmployeePortalSignInsLive
            WHERE LoginAt >= DATEADD(hour, -24, CONVERT(datetime2(0), SYSDATETIMEOFFSET() AT TIME ZONE 'Arabian Standard Time'))
            GROUP BY AccountName HAVING COUNT(DISTINCT DeviceId) > 1) x
            """);
        Assert.Equal(4, planted); // the four planted accounts at any hour (Dubai time, like the rule): normal staff keep one device on the last days
    }

    [Fact]
    public async Task Seed_IsIdempotentAndLeavesOtherOrganizationsUntouched()
    {
        const string countsSql =
            """
            SELECT CONCAT((SELECT COUNT(*) FROM dbo.Organizations), ',', (SELECT COUNT(*) FROM dbo.Roles), ',', (SELECT COUNT(*) FROM dbo.OrganizationUsers), ',',
                          (SELECT COUNT(*) FROM dbo.Applications), ',', (SELECT COUNT(*) FROM dbo.DatabaseColumns), ',', (SELECT COUNT(*) FROM dbo.DatabaseRelationships), ',',
                          (SELECT COUNT(*) FROM dbo.RoleTablePermissions), ',', (SELECT COUNT(*) FROM dbo.RoleColumnPermissions), ',', (SELECT COUNT(*) FROM dbo.RoleRowPermissions))
            """;
        var before = await _fixture.ScalarAsync<string>(countsSql);
        await _fixture.RunScriptAsync("04-seed-wabiinsightdb.sql");
        Assert.Equal(before, await _fixture.ScalarAsync<string>(countsSql));

        Assert.Equal(1, await _fixture.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Organizations WHERE OrganizationCode = N'GAS'"));
        Assert.Equal(0, await _fixture.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.OrganizationUsers ou JOIN dbo.Organizations o ON o.OrganizationId = ou.OrganizationId WHERE o.OrganizationCode <> N'GAS' AND ou.Email LIKE N'%@gulfaviation.example'"));
        foreach (var organizationId in new[] { _fixture.Database.Data.OrganizationAId, _fixture.Database.Data.OrganizationBId })
        {
            Assert.Equal(0, await _fixture.ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.Applications WHERE OrganizationId = '{organizationId}' AND ApplicationCode IN (N'FLIGHT-OPS', N'HR-PAYROLL', N'PROCUREMENT')"));
        }
    }

    public sealed class Fixture : IAsyncLifetime
    {
        private static readonly Regex GoSeparator = new(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

        public SqlServerTestDatabase Database { get; } = new();

        public string FlightDb => $"FlightOperationsDB_{_suffix}";

        public string HrDb => $"HumanResourcesDB_{_suffix}";

        public string ProcDb => $"ProcurementStoresDB_{_suffix}";

        public async Task InitializeAsync()
        {
            await Database.InitializeAsync();
            foreach (var script in new[] { "01-create-flightoperationsdb.sql", "02-create-humanresourcesdb.sql", "03-create-procurementstoresdb.sql", "04-seed-wabiinsightdb.sql" })
            {
                await RunScriptAsync(script);
            }
        }

        public async Task DisposeAsync()
        {
            try
            {
                await using var connection = await OpenAsync("master");
                foreach (var db in new[] { FlightDb, HrDb, ProcDb })
                {
                    await using var command = new SqlCommand($"IF DB_ID(N'{db}') IS NOT NULL BEGIN ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{db}]; END", connection);
                    await command.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                await Database.DisposeAsync();
            }
        }

        public async Task RunScriptAsync(string fileName)
        {
            var text = await File.ReadAllTextAsync(Path.Combine(SamplesDirectory(), fileName));
            text = text
                .Replace("FlightOperationsDB", FlightDb, StringComparison.Ordinal)
                .Replace("HumanResourcesDB", HrDb, StringComparison.Ordinal)
                .Replace("ProcurementStoresDB", ProcDb, StringComparison.Ordinal)
                .Replace("[WabiInsightDB]", $"[{Database.DatabaseName}]", StringComparison.Ordinal);

            await using var connection = await OpenAsync("master");
            foreach (var batch in GoSeparator.Split(text).Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                await using var command = new SqlCommand(batch, connection) { CommandTimeout = 600 };
                await using var reader = await command.ExecuteReaderAsync();
                do
                {
                    while (await reader.ReadAsync())
                    {
                    }
                }
                while (await reader.NextResultAsync());
            }
        }

        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = await OpenAsync(Database.DatabaseName);
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task<IReadOnlyList<object[]>> RowsAsync(string sql)
        {
            await using var connection = await OpenAsync(Database.DatabaseName);
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<object[]>();
            while (await reader.ReadAsync())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                rows.Add(values);
            }

            return rows;
        }

        private async Task<SqlConnection> OpenAsync(string database)
        {
            var connection = new SqlConnection(new SqlConnectionStringBuilder(Database.ConnectionString) { InitialCatalog = database }.ConnectionString);
            await connection.OpenAsync();
            return connection;
        }

        private static string SamplesDirectory()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "scripts", "sql", "samples");
                if (File.Exists(Path.Combine(directory.FullName, "WabiInsight.slnx")) && Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new DirectoryNotFoundException("scripts/sql/samples was not found above the test output directory.");
        }
    }
}
