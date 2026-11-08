using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using WabiInsight.Core.Entities.Monitoring;
using WabiInsight.Core.Monitoring;
using WabiInsight.Core.Security;
using WabiInsight.Infrastructure.Security;

namespace WabiInsight.IntegrationTests.Database;

/// <summary>
/// Runs scripts/sql/samples/wabi-bank 01–07 exactly as an operator would (batches split on GO, one connection per script)
/// against a freshly migrated configuration database that already holds the two test organizations. The five Wabi Bank
/// databases get a per-run suffix so parallel runs on a shared CI server never collide.
/// </summary>
public sealed class WabiBankSampleScriptTests : IClassFixture<WabiBankSampleScriptTests.Fixture>
{
    private const string InitialPassword = "Wabi#Sample#2026";
    private readonly Fixture _fixture;

    public WabiBankSampleScriptTests(Fixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Personnel_HasTheStandardBankStructure_WithStaffInEverySection()
    {
        var db = _fixture.Db("WabiBankPersonnelDB");
        Assert.Equal(8, await _fixture.ScalarAsync<int>($"SELECT COUNT(*) FROM [{db}].dbo.Divisions"));
        Assert.Equal(15, await _fixture.ScalarAsync<int>($"SELECT COUNT(*) FROM [{db}].dbo.Departments WHERE DepartmentType = N'HeadOffice'"));
        Assert.Equal(6, await _fixture.ScalarAsync<int>($"SELECT COUNT(*) FROM [{db}].dbo.Departments WHERE DepartmentType = N'Branch'"));
        Assert.Equal(0, await _fixture.ScalarAsync<int>(
            $"""
            SELECT COUNT(*) FROM [{db}].dbo.Departments d
            WHERE (SELECT COUNT(*) FROM [{db}].dbo.Sections s WHERE s.DepartmentCode = d.DepartmentCode) < CASE d.DepartmentType WHEN N'Branch' THEN 4 ELSE 2 END
            """));
        Assert.Equal(0, await _fixture.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [{db}].dbo.Sections s WHERE NOT EXISTS (SELECT 1 FROM [{db}].dbo.Employees e WHERE e.SectionCode = s.SectionCode AND e.EmploymentStatus = N'Active')"));
        Assert.Equal(478, await _fixture.ScalarAsync<int>($"SELECT COUNT(*) FROM [{db}].dbo.Employees"));

        // Leave still waiting for a decision at the year end (January and February 2026), never counted as 2025 absence.
        Assert.Equal(28, await _fixture.ScalarAsync<int>($"SELECT COUNT(*) FROM [{db}].dbo.LeaveRequests WHERE Status = N'Pending' AND DecidedByUserId IS NULL AND StartDate >= '2026-01-01'"));
    }

    [Fact]
    public async Task SourceDatabases_AreConsistentWithPersonnel_AndBalancesNeverGoNegative()
    {
        // 07-check-consistency.sql: every identifier of the four other systems exists in Personnel (or Core Banking for CIF).
        var rows = await _fixture.RowsAsync(_fixture.Script("07-check-consistency.sql"), "master");
        Assert.Empty(rows.Where(r => r.Length == 5 && (int)r[4] > 0).Select(r => string.Join('.', r)));
        var summary = rows[^1];
        Assert.True((int)summary[0] > 200, "07 should run a check per identifier column.");
        Assert.Equal(0, (int)summary[1]);

        var core = _fixture.Db("WabiBankCoreBankingDB");
        Assert.Equal(0, await _fixture.ScalarAsync<int>($"SELECT COUNT(*) FROM [{core}].dbo.Accounts WHERE BranchCode NOT LIKE N'BR-%'"));
        Assert.Equal(0, await _fixture.ScalarAsync<int>($"SELECT COUNT(*) FROM [{core}].dbo.Transactions WHERE BalanceAfter < 0"));
        Assert.True(await _fixture.ScalarAsync<int>($"SELECT COUNT(*) FROM [{core}].dbo.Transactions") > 500_000);

        // Counter cash looks like a branch: dozens of postings per teller a day, mostly small amounts.
        var perTellerDay = await _fixture.ScalarAsync<decimal>(
            $"SELECT CAST(COUNT(*) * 1.0 / COUNT(DISTINCT CONCAT(PostedByUserId, CAST(PostedAt AS date))) AS decimal(9,2)) FROM [{core}].dbo.Transactions WHERE Channel = N'Branch' AND TransactionCode IN (N'CDP', N'CWD')");
        Assert.InRange(perTellerDay, 20m, 60m);
        // Month-end loan balances: one row per active loan and month end, never more than the principal.
        Assert.Equal(0, await _fixture.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [{core}].dbo.MonthEndLoanBalances b JOIN [{core}].dbo.Loans l ON l.LoanNumber = b.LoanNumber WHERE b.OutstandingPrincipal > l.Principal OR b.OutstandingPrincipal < 0 OR b.BranchCode <> l.BranchCode"));

        // Nobody processes their own account; the planted exception is one Abu Dhabi teller's three postings.
        Assert.Equal(["KIYER", "KIYER", "KIYER"], await _fixture.ListAsync<string>(
            $"""
            SELECT t.PostedByUserId FROM [{core}].dbo.Transactions t JOIN [{core}].dbo.Accounts a ON a.AccountNumber = t.AccountNumber
            JOIN [{core}].dbo.Customers c ON c.CIF = a.CIF JOIN [{core}].dbo.Users u ON u.EmployeeNumber = c.StaffEmployeeNumber
            WHERE u.UserId IN (t.PostedByUserId, t.ApprovedByUserId) ORDER BY t.PostedAt
            """));
        Assert.Equal(0, await _fixture.ScalarAsync<int>(
            $"""
            SELECT COUNT(*) FROM [{core}].dbo.Accounts a JOIN [{core}].dbo.Customers c ON c.CIF = a.CIF JOIN [{core}].dbo.Users u ON u.EmployeeNumber = c.StaffEmployeeNumber
            WHERE u.UserId IN (a.OpenedByUserId, a.ApprovedByUserId, c.CreatedByUserId)
            """));
    }

    [Fact]
    public async Task Seed_CreatesEightUsers_FromPersonnel_InTheirRoles()
    {
        var users = await _fixture.RowsAsync(
            """
            SELECT ou.EmployeeNumber, r.RoleCode, d.DepartmentCode, ISNULL(s.SectionCode, N'-'), ou.MustChangePassword, ou.PasswordHash
            FROM dbo.OrganizationUsers ou
            JOIN dbo.Organizations o ON o.OrganizationId = ou.OrganizationId AND o.OrganizationCode = N'WBK'
            JOIN dbo.UserRoles ur ON ur.UserId = ou.UserId JOIN dbo.Roles r ON r.RoleId = ur.RoleId AND r.RoleCode <> N'Employee'
            JOIN dbo.Departments d ON d.DepartmentId = ou.DepartmentId LEFT JOIN dbo.Sections s ON s.SectionId = ou.SectionId
            ORDER BY ou.EmployeeNumber, r.RoleCode
            """);
        Assert.Equal(
            ["W1001:CyberSecurity:ITD:ITD-SEC", "W1001:OrganizationAdmin:ITD:ITD-SEC", "W1002:Management:CEO:-", "W1003:BranchManager:BR-DXB01:-", "W1004:Teller:BR-DXB01:BR-DXB01-TLR",
             "W1005:HROfficer:HRD:HRD-PSV", "W1006:BudgetController:FIN:FIN-BGP", "W1007:ComplianceOfficer:CMP:CMP-AML", "W1008:ServiceDeskManager:ITD:ITD-SD"],
            users.Select(u => $"{u[0]}:{u[1]}:{u[2]}:{u[3]}"));
        var hasher = new IdentityPasswordHashingService();
        Assert.All(users, u =>
        {
            Assert.True((bool)u[4]);
            Assert.Equal(PasswordVerification.Success, hasher.Verify((string)u[5], InitialPassword));
        });
        Assert.Equal(8, await _fixture.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.UserRoles ur JOIN dbo.Roles r ON r.RoleId = ur.RoleId AND r.RoleCode = N'Employee' JOIN dbo.Organizations o ON o.OrganizationId = r.OrganizationId AND o.OrganizationCode = N'WBK'"));
    }

    [Fact]
    public async Task Seed_CataloguesAllFiveSystems_AndGrantsLeastPrivilege()
    {
        foreach (var (code, name) in new[] { ("PERSONNEL", "WabiBankPersonnelDB"), ("CORE-BANKING", "WabiBankCoreBankingDB"), ("FINANCE", "WabiBankFinanceDB"), ("DIGITAL-BANKING", "WabiBankDigitalDB"), ("IT-HELPDESK", "WabiBankITDB") })
        {
            var db = _fixture.Db(name);
            var sourceColumns = await _fixture.ScalarAsync<int>(
                $"SELECT COUNT(*) FROM [{db}].sys.columns c JOIN [{db}].sys.objects o ON o.object_id = c.object_id WHERE o.type IN ('U','V') AND o.is_ms_shipped = 0 AND o.schema_id = 1");
            Assert.Equal(sourceColumns, await _fixture.ScalarAsync<int>(
                $"""
                SELECT COUNT(*) FROM dbo.DatabaseColumns c JOIN dbo.DatabaseObjects o ON o.ObjectId = c.ObjectId JOIN dbo.Applications a ON a.ApplicationId = o.ApplicationId
                JOIN dbo.Organizations org ON org.OrganizationId = a.OrganizationId AND org.OrganizationCode = N'WBK' WHERE a.ApplicationCode = N'{code}'
                """));
            Assert.Equal(0, await _fixture.ScalarAsync<int>(
                $"""
                SELECT COUNT(*) FROM dbo.DatabaseObjects o JOIN dbo.Applications a ON a.ApplicationId = o.ApplicationId
                JOIN dbo.Organizations org ON org.OrganizationId = a.OrganizationId AND org.OrganizationCode = N'WBK' WHERE a.ApplicationCode = N'{code}' AND o.BusinessDescription IS NULL
                """));
        }

        // Every business application carries example questions in both languages; the built-in one keeps none.
        Assert.Equal(5, await _fixture.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Applications a JOIN dbo.Organizations org ON org.OrganizationId = a.OrganizationId AND org.OrganizationCode = N'WBK' " +
            "WHERE a.IsBuiltIn = 0 AND (SELECT COUNT(*) FROM OPENJSON(a.ExampleQuestionsJson) WITH (en nvarchar(200) '$.en', ar nvarchar(200) '$.ar') e WHERE e.en IS NOT NULL AND e.ar IS NOT NULL) >= 3"));

        const string wbkRoles = "JOIN dbo.Roles r ON r.RoleId = p.RoleId JOIN dbo.Organizations org ON org.OrganizationId = r.OrganizationId AND org.OrganizationCode = N'WBK'";
        // Only Management and Compliance may read Restricted columns (Emirates ID, passport, IBAN, KYC numbers).
        Assert.Equal(0, await _fixture.ScalarAsync<int>(
            $"""
            SELECT COUNT(*) FROM dbo.RoleTablePermissions p {wbkRoles}
            JOIN dbo.DatabaseColumns c ON c.ObjectId = p.ObjectId AND c.Classification = N'Restricted'
            WHERE r.RoleCode <> N'ComplianceOfficer'
              AND (p.AllColumns = 1 OR EXISTS (SELECT 1 FROM dbo.RoleColumnPermissions cp WHERE cp.RoleId = p.RoleId AND cp.ColumnId = c.ColumnId))
            """));
        // The IT Service Desk Manager reads no banking, digital or finance data; the HR officer only Personnel and own tickets.
        Assert.Equal(0, await _fixture.ScalarAsync<int>(
            $"""
            SELECT COUNT(*) FROM dbo.RoleTablePermissions p {wbkRoles} JOIN dbo.DatabaseObjects o ON o.ObjectId = p.ObjectId JOIN dbo.Applications a ON a.ApplicationId = o.ApplicationId
            WHERE (r.RoleCode = N'ServiceDeskManager' AND a.ApplicationCode NOT IN (N'IT-HELPDESK', N'PERSONNEL'))
               OR (r.RoleCode = N'HROfficer' AND a.ApplicationCode <> N'PERSONNEL')
               OR (r.RoleCode = N'Teller' AND a.ApplicationCode <> N'CORE-BANKING')
            """));
        var scoped = await _fixture.RowsAsync(
            $"""
            SELECT r.RoleCode, rp.Scope, COUNT(*) FROM dbo.RoleRowPermissions rp JOIN dbo.Roles r ON r.RoleId = rp.RoleId
            JOIN dbo.Organizations org ON org.OrganizationId = r.OrganizationId AND org.OrganizationCode = N'WBK'
            WHERE r.RoleCode IN (N'Teller', N'Employee') GROUP BY r.RoleCode, rp.Scope ORDER BY 1, 2
            """);
        Assert.Equal(["Employee:Self:3", "Teller:All:2", "Teller:Self:2"], scoped.Select(s => $"{s[0]}:{s[1]}:{s[2]}"));
        // Every scoped grant has the mapping column it needs (branch staff: the department code is the branch code).
        Assert.Equal(0, await _fixture.ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM dbo.RoleRowPermissions rp LEFT JOIN dbo.RowScopeColumnMappings m ON m.ObjectId = rp.ObjectId
            WHERE (rp.Scope = N'Self' AND m.EmployeeColumnId IS NULL) OR (rp.Scope = N'Department' AND m.DepartmentColumnId IS NULL)
            """));
        Assert.Equal(26, await _fixture.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.RoleRowPermissions p {wbkRoles} WHERE r.RoleCode = N'BranchManager' AND p.Scope = N'Department'"));
        // Directory roles read the employee directory and structure without Confidential personal data (dates of birth, mobiles, salary bands).
        Assert.Equal(0, await _fixture.ScalarAsync<int>(
            $"""
            SELECT COUNT(*) FROM dbo.RoleTablePermissions p {wbkRoles}
            JOIN dbo.DatabaseObjects o ON o.ObjectId = p.ObjectId JOIN dbo.Applications a ON a.ApplicationId = o.ApplicationId AND a.ApplicationCode = N'PERSONNEL'
            JOIN dbo.DatabaseColumns c ON c.ObjectId = p.ObjectId AND c.Classification IN (N'Confidential', N'Restricted')
            WHERE r.RoleCode IN (N'ServiceDeskManager', N'BudgetController', N'CyberSecurity')
              AND (p.AllColumns = 1 OR EXISTS (SELECT 1 FROM dbo.RoleColumnPermissions cp WHERE cp.RoleId = p.RoleId AND cp.ColumnId = c.ColumnId))
            """));
        Assert.True(await _fixture.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.RoleColumnPermissions cp JOIN dbo.Roles r ON r.RoleId = cp.RoleId AND r.RoleCode = N'HROfficer' JOIN dbo.DatabaseColumns c ON c.ColumnId = cp.ColumnId AND c.Classification = N'Confidential'") > 0);

        // The planner reads descriptions, never data: codes and short coded values are listed where it needs them.
        string Description(string objectName, string column) => $"""
            SELECT c.BusinessDescription FROM dbo.DatabaseColumns c JOIN dbo.DatabaseObjects o ON o.ObjectId = c.ObjectId
            JOIN dbo.Applications a ON a.ApplicationId = o.ApplicationId JOIN dbo.Organizations org ON org.OrganizationId = a.OrganizationId AND org.OrganizationCode = N'WBK'
            WHERE o.ObjectName = N'{objectName}' AND c.ColumnName = N'{column}'
            """;
        Assert.Contains("ITD Information Technology", await _fixture.ScalarAsync<string>(Description("BudgetLines", "DepartmentCode")), StringComparison.Ordinal);
        Assert.Contains("BR-DXB01 Deira", await _fixture.ScalarAsync<string>(Description("vw_TransactionsByStaff", "PostingUnitCode")), StringComparison.Ordinal);
        Assert.Contains("C = credit", await _fixture.ScalarAsync<string>(Description("vw_TransactionsByStaff", "Direction")), StringComparison.Ordinal);
        Assert.EndsWith("Values: Approved, Cancelled, Pending, Rejected.", await _fixture.ScalarAsync<string>(Description("LeaveRequests", "Status")), StringComparison.Ordinal);
        Assert.Equal(0, await _fixture.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.DatabaseColumns WHERE Classification IN (N'Confidential', N'Restricted') AND BusinessDescription LIKE N'%Values: %'"));
    }

    [Fact]
    public async Task Seed_MonitoringRules_AreValid_AndEachPlantedFindingIsInItsLiveWindow()
    {
        var rules = await _fixture.RowsAsync(
            """
            SELECT mr.Name, mr.Kind, mr.DefinitionJson, owner.EmployeeNumber, r.RoleCode FROM dbo.MonitoringRules mr
            JOIN dbo.Organizations o ON o.OrganizationId = mr.OrganizationId AND o.OrganizationCode = N'WBK'
            JOIN dbo.OrganizationUsers owner ON owner.UserId = mr.OwnerUserId
            JOIN dbo.MonitoringRuleRecipients rr ON rr.RuleId = mr.RuleId JOIN dbo.Roles r ON r.RoleId = rr.RoleId
            ORDER BY mr.Name
            """);
        Assert.Equal(5, rules.Count);
        foreach (var rule in rules)
        {
            var kind = Enum.Parse<MonitoringRuleKind>((string)rule[1]);
            var definition = RuleDefinition.Parse((string)rule[2]);
            Assert.Empty(RuleDefinitionValidator.Validate(kind, definition));
            IEnumerable<Guid> ids = kind == MonitoringRuleKind.Data
                ? new[] { definition.Data!.TimeColumnId }.OfType<Guid>().Concat(definition.Data.GroupByColumnIds).Concat(definition.Data.Filters.Select(f => f.ColumnId)).ToList()
                : [];
            foreach (var id in ids)
            {
                Assert.Equal(1, await _fixture.ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.DatabaseColumns WHERE ColumnId = '{id}' AND ObjectId = '{definition.Data!.ObjectId}'"));
            }
        }

        Assert.Equal(
            ["Cash deposits of AED 55,000 or more in a day (individuals):W1007:ComplianceOfficer", "KYC documents looked up outside Compliance:W1001:ComplianceOfficer",
             "More than three incidents on one configuration item in 7 days:W1008:ServiceDeskManager", "More than two returned cheques per account in 30 days:W1007:ComplianceOfficer",
             "Same digital profile, several devices in a day:W1001:CyberSecurity"],
            rules.Select(r => $"{r[0]}:{r[3]}:{r[4]}"));

        // The planted cases, read like the rules read them (Dubai time, window ending now).
        const string now = "CONVERT(datetime2(0), SYSDATETIMEOFFSET() AT TIME ZONE 'Arabian Standard Time')";
        Assert.Equal(["anjali00484"], await _fixture.ListAsync<string>(
            $"SELECT Username FROM [{_fixture.Db("WabiBankDigitalDB")}].dbo.vw_SignInsLive WHERE SignInAt > DATEADD(HOUR, -24, {now}) AND SignInAt <= {now} GROUP BY Username HAVING COUNT(DISTINCT DeviceId) > 2"));
        Assert.Equal(["C100503"], await _fixture.ListAsync<string>(
            $"""
            SELECT CIF FROM [{_fixture.Db("WabiBankCoreBankingDB")}].dbo.vw_TransactionsLive
            WHERE TransactionCode = N'CDP' AND CustomerType = N'Individual' AND PostedAt > DATEADD(HOUR, -24, {now}) AND PostedAt <= {now}
            GROUP BY CIF HAVING SUM(Amount) >= 55000
            """));
        Assert.Equal(2, (await _fixture.ListAsync<string>(
            $"SELECT AccountNumber FROM [{_fixture.Db("WabiBankCoreBankingDB")}].dbo.vw_InwardChequesLive WHERE Status = N'Returned' AND PresentedDate > DATEADD(HOUR, -720, {now}) GROUP BY AccountNumber HAVING COUNT(*) > 2")).Count);
        Assert.Equal(["ATM-SHJ01-02"], await _fixture.ListAsync<string>(
            $"SELECT CiCode FROM [{_fixture.Db("WabiBankITDB")}].dbo.vw_IncidentsLive WHERE CiCode IS NOT NULL AND OpenedAt > DATEADD(HOUR, -168, {now}) AND OpenedAt <= {now} GROUP BY CiCode HAVING COUNT(*) > 3"));
    }

    [Fact]
    public async Task Seed_IsIdempotent_AndLeavesOtherOrganizationsUntouched()
    {
        const string countsSql =
            """
            SELECT CONCAT((SELECT COUNT(*) FROM dbo.Organizations), ',', (SELECT COUNT(*) FROM dbo.Roles), ',', (SELECT COUNT(*) FROM dbo.OrganizationUsers), ',',
                          (SELECT COUNT(*) FROM dbo.Applications), ',', (SELECT COUNT(*) FROM dbo.DatabaseColumns), ',', (SELECT COUNT(*) FROM dbo.DatabaseRelationships), ',',
                          (SELECT COUNT(*) FROM dbo.RoleTablePermissions), ',', (SELECT COUNT(*) FROM dbo.RoleColumnPermissions), ',', (SELECT COUNT(*) FROM dbo.RoleRowPermissions), ',',
                          (SELECT COUNT(*) FROM dbo.MonitoringRules), ',', (SELECT COUNT(*) FROM dbo.Departments), ',', (SELECT COUNT(*) FROM dbo.Sections), ',',
                          (SELECT CHECKSUM_AGG(CHECKSUM(BusinessDescription)) FROM dbo.DatabaseColumns), ',', (SELECT SUM(CAST(LEN(BusinessDescription) AS bigint)) FROM dbo.DatabaseColumns))
            """;
        var before = await _fixture.ScalarAsync<string>(countsSql);
        await _fixture.RunScriptAsync("06-seed-wabiinsightdb.sql");
        Assert.Equal(before, await _fixture.ScalarAsync<string>(countsSql));

        Assert.Equal((21, 69), (await _fixture.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Departments d JOIN dbo.Organizations o ON o.OrganizationId = d.OrganizationId AND o.OrganizationCode = N'WBK'"),
                                await _fixture.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Sections s JOIN dbo.Organizations o ON o.OrganizationId = s.OrganizationId AND o.OrganizationCode = N'WBK'")));
        foreach (var organizationId in new[] { _fixture.Database.Data.OrganizationAId, _fixture.Database.Data.OrganizationBId })
        {
            Assert.Equal(0, await _fixture.ScalarAsync<int>(
                $"SELECT COUNT(*) FROM dbo.Applications WHERE OrganizationId = '{organizationId}' AND ApplicationCode IN (N'PERSONNEL', N'CORE-BANKING', N'FINANCE', N'DIGITAL-BANKING', N'IT-HELPDESK')"));
            Assert.Equal(0, await _fixture.ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.OrganizationUsers WHERE OrganizationId = '{organizationId}' AND Email LIKE N'%@wabibank.example'"));
        }
    }

    public sealed class Fixture : IAsyncLifetime
    {
        private static readonly Regex GoSeparator = new(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly string[] Databases = ["WabiBankPersonnelDB", "WabiBankCoreBankingDB", "WabiBankFinanceDB", "WabiBankDigitalDB", "WabiBankITDB"];
        private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

        public SqlServerTestDatabase Database { get; } = new();

        public string Db(string name) => $"{name}_{_suffix}";

        public async Task InitializeAsync()
        {
            await Database.InitializeAsync();
            foreach (var script in new[] { "01-create-wabibankpersonneldb.sql", "02-create-wabibankcorebankingdb.sql", "03-create-wabibankfinancedb.sql", "04-create-wabibankdigitaldb.sql", "05-create-wabibankitdb.sql", "06-seed-wabiinsightdb.sql" })
            {
                await RunScriptAsync(script);
            }
        }

        public async Task DisposeAsync()
        {
            try
            {
                await using var connection = await OpenAsync("master");
                foreach (var db in Databases.Select(Db))
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

        /// <summary>The script's text with this run's database names.</summary>
        public string Script(string fileName)
        {
            var text = File.ReadAllText(Path.Combine(SamplesDirectory(), fileName));
            foreach (var name in Databases)
            {
                text = text.Replace(name, Db(name), StringComparison.Ordinal);
            }

            return text.Replace("[WabiInsightDB]", $"[{Database.DatabaseName}]", StringComparison.Ordinal);
        }

        public async Task RunScriptAsync(string fileName)
        {
            await using var connection = await OpenAsync("master");
            foreach (var batch in GoSeparator.Split(Script(fileName)).Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                await using var command = new SqlCommand(batch, connection) { CommandTimeout = 900 };
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

        public async Task<IReadOnlyList<T>> ListAsync<T>(string sql) => (await RowsAsync(sql)).Select(r => (T)r[0]).ToList();

        public async Task<IReadOnlyList<object[]>> RowsAsync(string sql, string? database = null)
        {
            await using var connection = await OpenAsync(database ?? Database.DatabaseName);
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 300 };
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<object[]>();
            do
            {
                while (await reader.ReadAsync())
                {
                    var values = new object[reader.FieldCount];
                    reader.GetValues(values);
                    rows.Add(values);
                }
            }
            while (await reader.NextResultAsync());

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
                var candidate = Path.Combine(directory.FullName, "scripts", "sql", "samples", "wabi-bank");
                if (File.Exists(Path.Combine(directory.FullName, "WabiInsight.slnx")) && Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new DirectoryNotFoundException("scripts/sql/samples/wabi-bank was not found above the test output directory.");
        }
    }
}
