using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Infrastructure.Databases;
using WabiInsight.IntegrationTests.Ai;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.OracleSource;

/// <summary>
/// P13.30 (A2): Oracle end to end against a real database. Runs when WI_TEST_ORACLE_CONNECTION is set to an administrator
/// connection (for example SYSTEM on Oracle Free's FREEPDB1); CI runs Oracle Free as a service container. The fixture
/// creates an application schema owner and a separate login that may only SELECT from it, which is how organizations
/// are asked to connect.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class OracleEndToEndTests : IDisposable
{
    private const string Owner = "WI_E2E_APP";
    private const string Reader = "WI_E2E_RO";

    private static readonly string? AdminConnection = Environment.GetEnvironmentVariable("WI_TEST_ORACLE_CONNECTION");
    private static readonly SemaphoreSlim FixtureLock = new(1, 1);
    private static (string Owner, string Reader)? _fixture;

    private readonly SqlServerTestDatabase _db;
    private readonly AiApiFactory _factory;

    public OracleEndToEndTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new AiApiFactory(db.ConnectionString, new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "wi-keys-tests", "oracle-e2e"), ["Ai:Anthropic:ApiKey"] = "sk-ant-seeded-platform-key-0003" });
    }

    public void Dispose() => _factory.Dispose();

    /// <summary>The orders the fixture inserts: id, branch, date (with a time of day), amount, quantity, department.</summary>
    private static readonly (int Id, string Branch, DateTime Date, decimal Amount, int Quantity, string Dept)[] Orders =
    [
        (1, "DXB", new DateTime(2026, 1, 5, 9, 30, 0), 1200.50m, 1, "FIN"),
        (2, "DXB", new DateTime(2026, 2, 14, 16, 0, 0), 800.00m, 1, "FIN"),
        (3, "DXB", new DateTime(2026, 3, 31, 15, 45, 0), 450.25m, 2, "FIN"),   // last day of the quarter, afternoon
        (4, "SHJ", new DateTime(2026, 1, 20, 11, 0, 0), 3000.00m, 4, "FIN"),
        (5, "SHJ", new DateTime(2026, 2, 2, 8, 0, 0), 150.00m, 7, "OPS"),     // another department: never visible to the analyst
        (6, "AUH", new DateTime(2026, 4, 1, 0, 0, 0), 999.99m, 3, "FIN"),     // after the quarter
    ];

    private static async Task<(string OwnerConnection, string ReaderConnection)> FixtureAsync()
    {
        await FixtureLock.WaitAsync();
        try
        {
            if (_fixture is { } ready)
            {
                return ready;
            }

            var admin = new OracleConnectionStringBuilder(AdminConnection);
            var ownerPassword = "Own" + Guid.NewGuid().ToString("N")[..12];
            var readerPassword = "Rd" + Guid.NewGuid().ToString("N")[..12];
            await using (var connection = new OracleConnection(AdminConnection))
            {
                await connection.OpenAsync();
                foreach (var user in new[] { Reader, Owner })
                {
                    await ExecuteAsync(connection, $"BEGIN EXECUTE IMMEDIATE 'DROP USER {user} CASCADE'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -1918 THEN RAISE; END IF; END;");
                }

                await ExecuteAsync(connection, $"CREATE USER {Owner} IDENTIFIED BY \"{ownerPassword}\" QUOTA UNLIMITED ON USERS");
                await ExecuteAsync(connection, $"GRANT CREATE SESSION, CREATE TABLE, CREATE VIEW TO {Owner}");
                await ExecuteAsync(connection, $"CREATE USER {Reader} IDENTIFIED BY \"{readerPassword}\"");
                await ExecuteAsync(connection, $"GRANT CREATE SESSION TO {Reader}");
            }

            var ownerConnection = new OracleConnectionStringBuilder(AdminConnection) { UserID = Owner, Password = ownerPassword }.ConnectionString;
            await using (var connection = new OracleConnection(ownerConnection))
            {
                await connection.OpenAsync();
                await ExecuteAsync(connection, "CREATE TABLE BRANCHES (BRANCH_CODE VARCHAR2(10) PRIMARY KEY, BRANCH_NAME VARCHAR2(100) NOT NULL, \"اسم الفرع\" NVARCHAR2(100))");
                await ExecuteAsync(connection, "CREATE TABLE ORDERS (ORDER_ID NUMBER(10) PRIMARY KEY, BRANCH_CODE VARCHAR2(10) NOT NULL REFERENCES BRANCHES (BRANCH_CODE), \"Order Date\" DATE NOT NULL, AMOUNT NUMBER(14,2) NOT NULL, QUANTITY NUMBER(5) NOT NULL, DEPT_CODE VARCHAR2(10) NOT NULL)");
                await ExecuteAsync(connection, "CREATE VIEW V_BRANCH_TOTALS AS SELECT BRANCH_CODE, SUM(AMOUNT) AS TOTAL_AMOUNT FROM ORDERS GROUP BY BRANCH_CODE");
                foreach (var (code, name, arabic) in new[] { ("DXB", "Dubai", "دبي"), ("SHJ", "Sharjah", "الشارقة"), ("AUH", "Abu Dhabi", "أبوظبي") })
                {
                    await ExecuteAsync(connection, "INSERT INTO BRANCHES VALUES (:c, :n, :a)", ("c", code), ("n", name), ("a", arabic));
                }

                foreach (var o in Orders)
                {
                    await ExecuteAsync(connection, "INSERT INTO ORDERS VALUES (:i, :b, :d, :a, :q, :p)", ("i", o.Id), ("b", o.Branch), ("d", o.Date), ("a", o.Amount), ("q", o.Quantity), ("p", o.Dept));
                }

                await ExecuteAsync(connection, "COMMIT");
                foreach (var table in new[] { "BRANCHES", "ORDERS", "V_BRANCH_TOTALS" })
                {
                    await ExecuteAsync(connection, $"GRANT SELECT ON {table} TO {Reader}");
                }
            }

            var readerConnection = new OracleConnectionStringBuilder(AdminConnection) { UserID = Reader, Password = readerPassword }.ConnectionString;
            _fixture = (ownerConnection, readerConnection);
            return _fixture.Value;
        }
        finally
        {
            FixtureLock.Release();
        }
    }

    private static async Task ExecuteAsync(OracleConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.BindByName = true;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(new OracleParameter(name, value));
        }

        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task ReadOnlyLogin_IsVerified_AndDiscoversTheGrantedSchema_WhileTheOwnerIsNotReadOnly()
    {
        if (string.IsNullOrWhiteSpace(AdminConnection))
        {
            return; // gated: CI and a local Oracle Free set WI_TEST_ORACLE_CONNECTION
        }

        var (ownerConnection, readerConnection) = await FixtureAsync();
        var adapter = new OracleAdapter();

        var reader = await adapter.TestAsync(readerConnection);
        Assert.True(reader.Succeeded, reader.Error);
        Assert.Equal(Reader, reader.DatabaseName);
        Assert.True(reader.ReadOnlyVerified);
        Assert.Empty(reader.Warnings);

        // The schema owner may change its own tables without any grant: never reported as read-only (A2).
        var owner = await adapter.TestAsync(ownerConnection);
        Assert.True(owner.Succeeded, owner.Error);
        Assert.False(owner.ReadOnlyVerified);
        Assert.Contains("account_not_read_only", owner.Warnings);

        // Discovery as the read-only login finds the application schema it may read, and nothing maintained by Oracle.
        var schema = await adapter.DiscoverAsync(readerConnection);
        Assert.DoesNotContain(schema.Objects, o => o.SchemaName is "SYS" or "SYSTEM" or "XDB" or "MDSYS");
        var orders = Assert.Single(schema.Objects, o => o.SchemaName == Owner && o.ObjectName == "ORDERS");
        Assert.Equal(["ORDER_ID", "BRANCH_CODE", "Order Date", "AMOUNT", "QUANTITY", "DEPT_CODE"], orders.Columns.OrderBy(c => c.OrdinalPosition).Select(c => c.ColumnName));
        Assert.True(orders.Columns.Single(c => c.ColumnName == "ORDER_ID").IsPrimaryKey);
        Assert.Equal(Core.Entities.Metadata.DatabaseObjectType.View, schema.Objects.Single(o => o.SchemaName == Owner && o.ObjectName == "V_BRANCH_TOTALS").ObjectType);
        Assert.Contains(schema.Objects.Single(o => o.SchemaName == Owner && o.ObjectName == "BRANCHES").Columns, c => c.ColumnName == "اسم الفرع");
        Assert.Contains(schema.Relationships, r => r.FromSchema == Owner && r.FromObject == "ORDERS" && r.FromColumn == "BRANCH_CODE" && r.ToObject == "BRANCHES" && r.ToColumn == "BRANCH_CODE");
    }

    [Fact]
    public async Task AQuestion_RunsReadOnlyOnOracle_WithTheAnalystsDepartment_ArabicAndSpacedNames_AndLongAverages()
    {
        if (string.IsNullOrWhiteSpace(AdminConnection))
        {
            return;
        }

        var (_, readerConnection) = await FixtureAsync();
        FakeAiProvider.Script = null;
        try
        {
            using var alice = await LoginAsync(TestData.AliceEmail);

            // The application connects with the read-only login; the API itself verifies it and discovers the schema.
            var tag = $"ora{Guid.NewGuid():N}"[..12];
            var created = await alice.PostJsonAsync("/api/applications", new { applicationCode = tag, applicationName = "Oracle orders", applicationNameAr = "طلبات أوراكل", databaseType = "Oracle", connectionString = readerConnection });
            Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
            var applicationId = (await created.ReadJsonAsync()).GetProperty("applicationId").GetGuid();
            var test = await (await alice.PostJsonAsync($"/api/applications/{applicationId}/test-connection", new { })).ReadJsonAsync();
            Assert.True(test.GetProperty("readOnlyVerified").GetBoolean(), test.GetRawText());
            Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/applications/{applicationId}/approval", new { isApproved = true })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/metadata/applications/{applicationId}/refresh", new { })).StatusCode);
            var objects = (await (await alice.GetAsync($"/api/metadata/applications/{applicationId}/objects?pageSize=50")).ReadJsonAsync()).GetProperty("items").EnumerateArray().ToList();
            Guid ObjectId(string name) => objects.Single(o => o.GetProperty("schemaName").GetString() == Owner && o.GetProperty("objectName").GetString() == name).GetProperty("objectId").GetGuid();
            var ordersColumns = (await (await alice.PostJsonAsync($"/api/metadata/objects/{ObjectId("ORDERS")}/approve", new { approve = true })).ReadJsonAsync()).GetProperty("columns").EnumerateArray().ToList();
            Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/metadata/objects/{ObjectId("BRANCHES")}/approve", new { approve = true })).StatusCode);

            // An analyst of department FIN: Department scope on ORDERS (mapped on DEPT_CODE), every branch.
            var roles = (await (await alice.GetAsync("/api/roles")).ReadJsonAsync()).EnumerateArray().ToList();
            var analystId = roles.Single(r => r.GetProperty("roleCode").GetString() == SystemRoles.Analyst).GetProperty("roleId").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await alice.PutAsJsonAsync(new Uri($"/api/permissions/roles/{analystId}/objects/{ObjectId("ORDERS")}", UriKind.Relative), new { granted = true, allColumns = true, rowScope = "Department" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await alice.PutAsJsonAsync(new Uri($"/api/permissions/roles/{analystId}/objects/{ObjectId("BRANCHES")}", UriKind.Relative), new { granted = true, allColumns = true, rowScope = "All" })).StatusCode);
            var deptColumn = ordersColumns.Single(c => c.GetProperty("columnName").GetString() == "DEPT_CODE").GetProperty("columnId").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await alice.PutAsJsonAsync(new Uri($"/api/permissions/objects/{ObjectId("ORDERS")}/row-mapping", UriKind.Relative), new { departmentColumnId = deptColumn })).StatusCode);
            var departments = (await (await alice.GetAsync("/api/departments")).ReadJsonAsync()).EnumerateArray().ToList();
            var fin = departments.FirstOrDefault(d => d.GetProperty("departmentCode").GetString() == "FIN");
            var finId = fin.ValueKind == JsonValueKind.Object
                ? fin.GetProperty("departmentId").GetGuid()
                : (await (await alice.PostJsonAsync("/api/departments", new { departmentCode = "FIN", departmentNameEn = "Finance", departmentNameAr = "المالية" })).ReadJsonAsync()).GetProperty("departmentId").GetGuid();
            var (userId, email) = await InsertUserAsync(_db.Data.OrganizationAId, analystId);
            Assert.Equal(HttpStatusCode.OK, (await alice.PutAsJsonAsync(new Uri($"/api/users/{userId}", UriKind.Relative), new { fullName = "Oracle Analyst", employeeNumber = "E-ORA", departmentId = finId, preferredLanguage = "en", roleCodes = new[] { "Analyst" } })).StatusCode);
            using var analyst = await LoginAsync(email);

            // First quarter by branch, with the Arabic branch name, totals, average quantity (1.333… has more than 28
            // digits on Oracle), a filter on a column whose name has a space, and ORDER BY spelled in another case.
            FakeAiProvider.Script = _ => $$"""
                {"objective":"First quarter orders by branch","source":{"schema":"{{Owner}}","object":"ORDERS","alias":"o"},
                 "joins":[{"schema":"{{Owner}}","object":"BRANCHES","alias":"b","type":"inner","on":[{"left":"o.BRANCH_CODE","right":"b.BRANCH_CODE"}]}],
                 "select":[{"alias":"branch_ar","column":"b.اسم الفرع"},{"alias":"Total","column":"o.AMOUNT","aggregate":"sum"},{"alias":"avg_qty","column":"o.QUANTITY","aggregate":"avg"},{"alias":"orders","aggregate":"count"}],
                 "filters":{"logic":"and","conditions":[{"column":"o.Order Date","operator":"between","values":["2026-01-01","2026-03-31"]}],"groups":[]},
                 "groupBy":["branch_ar"],"orderBy":[{"alias":"total","direction":"desc"}],"limit":10,
                 "visualization":{"type":"bar","x":"branch_ar","y":["Total"]},"assumptions":[]}
                """;
            var ask = await analyst.PostJsonAsync("/api/ai/ask", new { applicationId, question = "First quarter orders by branch", language = "ar", interpret = false });
            Assert.True(ask.StatusCode == HttpStatusCode.OK, await ask.Content.ReadAsStringAsync());
            var result = (await ask.ReadJsonAsync()).GetProperty("panels")[0].GetProperty("result");
            var rows = result.GetProperty("rows").EnumerateArray().Select(r => (Branch: r[0].GetString()!, Total: r[1].GetDecimal(), Avg: r[2].GetDouble(), Count: r[3].GetInt64())).ToList();

            // Expected from the fixture: FIN only (row scope), 1 January to 31 March including the afternoon of the 31st.
            var expected = Orders.Where(o => o.Dept == "FIN" && o.Date >= new DateTime(2026, 1, 1) && o.Date < new DateTime(2026, 4, 1))
                .GroupBy(o => o.Branch == "DXB" ? "دبي" : o.Branch == "SHJ" ? "الشارقة" : "أبوظبي")
                .Select(g => (Branch: g.Key, Total: g.Sum(o => o.Amount), Avg: g.Average(o => (double)o.Quantity), Count: (long)g.Count()))
                .OrderByDescending(g => g.Total).ToList();
            Assert.Equal(expected.Select(e => e.Branch), rows.Select(r => r.Branch));
            Assert.Equal(expected.Select(e => e.Total), rows.Select(r => r.Total));
            Assert.Equal(expected.Select(e => e.Count), rows.Select(r => r.Count));
            for (var i = 0; i < rows.Count; i++)
            {
                Assert.Equal(expected[i].Avg, rows[i].Avg, 6);
            }

            Assert.Equal(4m / 3m, (decimal)rows.Single(r => r.Branch == "دبي").Avg, 6);
            Assert.Equal(expected.Select(e => e.Branch), result.GetProperty("series")[0].GetProperty("categories").EnumerateArray().Select(c => c.GetString()));

            // The SQL that ran is kept for reference: the department predicate inside the ORDERS source, a bound parameter
            // per value, Oracle quoting and limits.
            await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
            {
                var metricId = (await ask.ReadJsonAsync()).GetProperty("analysisId").GetGuid();
                var log = await context.AnalysisQueryLogs.IgnoreQueryFilters().SingleAsync(l => l.MetricId == metricId);
                using var queries = JsonDocument.Parse(log.QueriesJson);
                var sql = queries.RootElement[0].GetProperty("sql").GetString()!;
                Assert.Contains($"FROM (SELECT * FROM \"{Owner}\".\"ORDERS\" WHERE \"DEPT_CODE\" = ?) \"o\"", sql, StringComparison.Ordinal);
                Assert.Contains("\"b\".\"اسم الفرع\" AS \"branch_ar\"", sql, StringComparison.Ordinal);
                Assert.Contains("ORDER BY \"Total\" DESC", sql, StringComparison.Ordinal);
                Assert.EndsWith("FETCH FIRST 11 ROWS ONLY", sql, StringComparison.Ordinal);
                Assert.DoesNotContain("FIN", sql, StringComparison.Ordinal);
            }

            // By month: Oracle date truncation, a count per month, the read-only login throughout.
            FakeAiProvider.Script = _ => $$"""
                {"objective":"Orders per month","source":{"schema":"{{Owner}}","object":"ORDERS","alias":"o"},"joins":[],
                 "select":[{"alias":"month","column":"o.Order Date","dateTrunc":"month"},{"alias":"orders","aggregate":"count"}],
                 "groupBy":["month"],"orderBy":[{"alias":"month","direction":"asc"}],"limit":12,"visualization":{"type":"line","x":"month","y":["orders"]},"assumptions":[]}
                """;
            var monthly = await analyst.PostJsonAsync("/api/ai/ask", new { applicationId, question = "Orders per month", language = "en", interpret = false });
            Assert.True(monthly.StatusCode == HttpStatusCode.OK, await monthly.Content.ReadAsStringAsync());
            var months = (await monthly.ReadJsonAsync()).GetProperty("panels")[0].GetProperty("result").GetProperty("rows").EnumerateArray()
                .Select(r => (Month: DateTime.Parse(r[0].GetString()!, CultureInfo.InvariantCulture), Count: r[1].GetInt64())).ToList();
            var expectedMonths = Orders.Where(o => o.Dept == "FIN").GroupBy(o => new DateTime(o.Date.Year, o.Date.Month, 1)).OrderBy(g => g.Key).Select(g => (Month: g.Key, Count: (long)g.Count())).ToList();
            Assert.Equal(expectedMonths, months);
        }
        finally
        {
            FakeAiProvider.Script = null;
        }
    }

    private async Task<HttpClient> LoginAsync(string email, string password = TestData.UserPassword)
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, password)).StatusCode);
        return client;
    }

    private async Task<(Guid UserId, string Email)> InsertUserAsync(Guid organizationId, Guid roleId)
    {
        var email = $"ora.{Guid.NewGuid():N}@org.test";
        var userId = Core.Common.Ids.New();
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var now = DateTimeOffset.UtcNow;
        context.OrganizationUsers.Add(TestData.NewUser(userId, organizationId, email, "Oracle User"));
        context.LoginIdentities.Add(TestData.Identity(email, Core.Tenancy.PrincipalType.OrganizationUser, userId, organizationId, now));
        context.UserRoles.Add(new UserRole { OrganizationId = organizationId, UserId = userId, RoleId = roleId, AssignedAt = now, AssignedByAdministratorId = _db.Data.AdministratorId });
        await context.SaveChangesAsync();
        return (userId, email);
    }
}
