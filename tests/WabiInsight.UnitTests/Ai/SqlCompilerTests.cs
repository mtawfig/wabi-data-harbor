using System.Text.Json;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Ai.Compilers;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Organizations;

namespace WabiInsight.UnitTests.Ai;

/// <summary>Compiler snapshot tests: the exact SQL each dialect emits for the same authorized plan.</summary>
public sealed class SqlCompilerTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid App = Guid.NewGuid();

    private static readonly Dictionary<string, string> OrderColumns = new(StringComparer.OrdinalIgnoreCase) { ["OrderId"] = "int", ["CustomerId"] = "int", ["Amount"] = "decimal", ["Status"] = "nvarchar", ["OrderDate"] = "datetime2", ["DeptCode"] = "nvarchar", ["IsPaid"] = "bit" };
    private static readonly Dictionary<string, string> CustomerColumns = new(StringComparer.OrdinalIgnoreCase) { ["CustomerId"] = "int", ["Name"] = "nvarchar", ["CreatedAt"] = "datetime2", ["CreditLimit"] = "decimal" };

    private static AuthorizedPlan Plan(string planJson, IReadOnlyList<RowPredicate>? orderPredicates = null, int limit = 100)
    {
        var plan = JsonSerializer.Deserialize<QueryPlan>(planJson, Json)!;
        return new AuthorizedPlan(plan, App,
        [
            new AuthorizedObject("o", Guid.NewGuid(), "dbo", "Orders", orderPredicates ?? [], OrderColumns),
            new AuthorizedObject("c", Guid.NewGuid(), "dbo", "Customers", [], CustomerColumns),
        ], limit);
    }

    private const string Aggregated = """
        {"objective":"x","source":{"schema":"dbo","object":"Orders","alias":"o"},
         "joins":[{"schema":"dbo","object":"Customers","alias":"c","type":"left","on":[{"left":"o.CustomerId","right":"c.CustomerId"}]}],
         "select":[{"alias":"customer","column":"c.Name","aggregate":null,"dateTrunc":null},{"alias":"month","column":"o.OrderDate","aggregate":null,"dateTrunc":"month"},{"alias":"total","column":"o.Amount","aggregate":"sum","dateTrunc":null},{"alias":"orders","column":null,"aggregate":"count","dateTrunc":null}],
         "filters":{"logic":"and","conditions":[{"column":"o.Status","operator":"in","values":["Paid","Shipped"]},{"column":"o.OrderDate","operator":"gte","values":["2026-01-01"]},{"column":"o.IsPaid","operator":"eq","values":["true"]}],"groups":[{"logic":"or","conditions":[{"column":"c.Name","operator":"contains","values":["100%"]},{"column":"c.Name","operator":"is_null","values":[]}],"groups":[]}]},
         "groupBy":["customer","month"],"orderBy":[{"alias":"total","direction":"desc"}],"limit":50,"visualization":null,"assumptions":[]}
        """;

    [Fact]
    public void SqlServer_EmitsParameterizedSelect_WithRowPredicatesInsideTheSourceDerivedTable()
    {
        var compiled = new SqlServerCompiler().Compile(Plan(Aggregated, [new RowPredicate("DeptCode", "@rs0", "FIN"), new RowPredicate("DeptCode", "@rs1", "OPS")], limit: 50));

        Assert.Equal(
            "SELECT TOP (51) [c].[Name] AS [customer], DATEFROMPARTS(YEAR([o].[OrderDate]), MONTH([o].[OrderDate]), 1) AS [month], SUM([o].[Amount]) AS [total], COUNT(*) AS [orders] " +
            "FROM (SELECT * FROM [dbo].[Orders] WHERE [DeptCode] = @p0 OR [DeptCode] = @p1) [o] LEFT JOIN [dbo].[Customers] [c] ON [o].[CustomerId] = [c].[CustomerId] " +
            "WHERE [o].[Status] IN (@p2, @p3) AND [o].[OrderDate] >= @p4 AND [o].[IsPaid] = @p5 AND ([c].[Name] LIKE @p6 ESCAPE '\\' OR [c].[Name] IS NULL) " +
            "GROUP BY [c].[Name], DATEFROMPARTS(YEAR([o].[OrderDate]), MONTH([o].[OrderDate]), 1) ORDER BY [total] DESC",
            compiled.Sql);
        Assert.Equal(["FIN", "OPS", "Paid", "Shipped"], compiled.Parameters.Take(4).Select(p => p.Value).ToArray());
        Assert.Equal(new DateTime(2026, 1, 1), compiled.Parameters[4].Value);
        Assert.Equal(true, compiled.Parameters[5].Value);
        Assert.Equal("%100\\%%", compiled.Parameters[6].Value);
        Assert.DoesNotContain("FIN", compiled.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@p0", compiled.RedactedPreview, StringComparison.Ordinal);
        Assert.Contains("[DeptCode] = ? OR [DeptCode] = ?", compiled.RedactedPreview, StringComparison.Ordinal);
        Assert.Equal(50, compiled.RowCap);
    }

    [Fact]
    public void Oracle_UsesDoubleQuotes_ColonParameters_TruncAndFetchFirst()
    {
        var compiled = new OracleCompiler().Compile(Plan(Aggregated, [new RowPredicate("DeptCode", "@rs0", "FIN")], limit: 50));
        Assert.StartsWith("SELECT \"c\".\"Name\" AS \"customer\", TRUNC(\"o\".\"OrderDate\", 'MM') AS \"month\", SUM(\"o\".\"Amount\") AS \"total\", COUNT(*) AS \"orders\" FROM (SELECT * FROM \"dbo\".\"Orders\" WHERE \"DeptCode\" = :p0) \"o\" LEFT JOIN \"dbo\".\"Customers\" \"c\" ON \"o\".\"CustomerId\" = \"c\".\"CustomerId\" WHERE \"o\".\"Status\" IN (:p1, :p2)", compiled.Sql, StringComparison.Ordinal);
        Assert.EndsWith("ORDER BY \"total\" DESC FETCH FIRST 51 ROWS ONLY", compiled.Sql, StringComparison.Ordinal);
        Assert.All(compiled.Parameters, p => Assert.StartsWith(":p", p.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void Having_FiltersAggregatedValues_WithTypedParameters_InBothDialects()
    {
        // "Customers with more than two orders, a total between 100 and 500.5 and a last order before March": conditions on totals.
        const string totals = """
            {"objective":"x","source":{"schema":"dbo","object":"Orders","alias":"o"},
             "joins":[{"schema":"dbo","object":"Customers","alias":"c","type":"inner","on":[{"left":"o.CustomerId","right":"c.CustomerId"}]}],
             "select":[{"alias":"customer","column":"c.Name","aggregate":null,"dateTrunc":null},{"alias":"orders","column":null,"aggregate":"count","dateTrunc":null},{"alias":"total","column":"o.Amount","aggregate":"sum","dateTrunc":null},
                       {"alias":"lastOrder","column":"o.OrderDate","aggregate":"max","dateTrunc":null},{"alias":"paid","column":"o.IsPaid","aggregate":"sum","dateTrunc":null}],
             "filters":{"logic":"and","conditions":[{"column":"o.Status","operator":"neq","values":["Cancelled"],"compareTo":null}],"groups":[]},
             "groupBy":["customer"],
             "having":[{"column":"orders","operator":"gt","values":["2"],"compareTo":""},{"column":"total","operator":"between","values":["100","500.5"],"compareTo":""},
                       {"column":"lastOrder","operator":"lt","values":["2026-03-01"],"compareTo":""},{"column":"paid","operator":"lt","values":[],"compareTo":"orders"}],
             "orderBy":[{"alias":"orders","direction":"desc"}],"limit":null,"visualization":null,"assumptions":[]}
            """;
        var compiled = new SqlServerCompiler().Compile(Plan(totals));
        Assert.Equal(
            "SELECT TOP (101) [c].[Name] AS [customer], COUNT(*) AS [orders], SUM([o].[Amount]) AS [total], MAX([o].[OrderDate]) AS [lastOrder], SUM(CAST([o].[IsPaid] AS INT)) AS [paid] " +
            "FROM [dbo].[Orders] [o] INNER JOIN [dbo].[Customers] [c] ON [o].[CustomerId] = [c].[CustomerId] WHERE [o].[Status] <> @p0 GROUP BY [c].[Name] " +
            "HAVING COUNT(*) > @p1 AND SUM([o].[Amount]) BETWEEN @p2 AND @p3 AND MAX([o].[OrderDate]) < @p4 AND SUM(CAST([o].[IsPaid] AS INT)) < COUNT(*) ORDER BY [orders] DESC",
            compiled.Sql);
        Assert.Equal(2L, compiled.Parameters[1].Value);
        Assert.Equal(100m, compiled.Parameters[2].Value);
        Assert.Equal(500.5m, compiled.Parameters[3].Value);
        Assert.Equal(new DateTime(2026, 3, 1), compiled.Parameters[4].Value);

        var oracle = new OracleCompiler().Compile(Plan(totals)).Sql;
        Assert.Contains("GROUP BY \"c\".\"Name\" HAVING COUNT(*) > :p1 AND SUM(\"o\".\"Amount\") BETWEEN :p2 AND :p3", oracle, StringComparison.Ordinal);
        Assert.EndsWith("ORDER BY \"orders\" DESC FETCH FIRST 101 ROWS ONLY", oracle, StringComparison.Ordinal);

        // A value that does not fit the aggregate is rejected, not concatenated.
        var bad = Plan(totals.Replace("[\"2\"]", "[\"two\"]", StringComparison.Ordinal));
        Assert.Equal("plan_filter_value_type_mismatch", Assert.Throws<DomainException>(() => new SqlServerCompiler().Compile(bad)).Message);
    }

    [Fact]
    public void ColumnComparison_ComparesTwoPermittedColumns_OfTheSameKindOnly()
    {
        static string Compare(string column, string op, string other) => $$"""
            {"objective":"x","source":{"schema":"dbo","object":"Orders","alias":"o"},
             "joins":[{"schema":"dbo","object":"Customers","alias":"c","type":"inner","on":[{"left":"o.CustomerId","right":"c.CustomerId"}]}],
             "select":[{"alias":"id","column":"o.OrderId","aggregate":null,"dateTrunc":null}],
             "filters":{"logic":"and","conditions":[{"column":"{{column}}","operator":"{{op}}","values":[],"compareTo":"{{other}}"},{"column":"o.Status","operator":"eq","values":["Open"],"compareTo":null}],"groups":[]},
             "groupBy":[],"having":[],"orderBy":[],"limit":null,"visualization":null,"assumptions":[]}
            """;

        var compiled = new SqlServerCompiler().Compile(Plan(Compare("o.OrderDate", "gt", "c.CreatedAt"), [new RowPredicate("DeptCode", "@rs0", "FIN")]));
        Assert.Contains("WHERE [o].[OrderDate] > [c].[CreatedAt] AND [o].[Status] = @p1", compiled.Sql, StringComparison.Ordinal);
        Assert.Equal(["FIN", "Open"], compiled.Parameters.Select(p => p.Value).ToArray());
        Assert.Contains("\"o\".\"Amount\" >= \"c\".\"CreditLimit\"", new OracleCompiler().Compile(Plan(Compare("o.Amount", "gte", "c.CreditLimit"))).Sql, StringComparison.Ordinal);

        // A date against a number, or a column the plan was not authorized for, never compiles.
        Assert.Equal("plan_compare_type_mismatch", Assert.Throws<DomainException>(() => new SqlServerCompiler().Compile(Plan(Compare("o.OrderDate", "gt", "c.CreditLimit")))).Message);
        Assert.Equal("column_not_permitted", Assert.Throws<DomainException>(() => new SqlServerCompiler().Compile(Plan(Compare("o.Amount", "gt", "c.Secret")))).Message);
    }

    [Fact]
    public void AskerPlaceholders_AreBoundToTheAskersOwnCode_AndNeverCompiledWithoutOne()
    {
        const string mine = """
            {"objective":"x","source":{"schema":"dbo","object":"Orders","alias":"o"},"joins":[],
             "select":[{"alias":"orders","column":null,"aggregate":"count","dateTrunc":null}],
             "filters":{"logic":"and","conditions":[{"column":"o.DeptCode","operator":"in","values":["$asker.department","OPS"],"compareTo":null}],"groups":[]},
             "groupBy":[],"having":[],"orderBy":[],"limit":null,"visualization":null,"assumptions":[]}
            """;
        var plan = Plan(mine) with { AskerValues = new Dictionary<string, string> { [AskerValues.Department] = "FIN" } };
        var compiled = new SqlServerCompiler().Compile(plan);
        Assert.Contains("WHERE [o].[DeptCode] IN (@p0, @p1)", compiled.Sql, StringComparison.Ordinal);
        Assert.Equal(["FIN", "OPS"], compiled.Parameters.Select(p => p.Value).ToArray());
        Assert.DoesNotContain("FIN", compiled.Sql, StringComparison.Ordinal);
        // The stored plan keeps the placeholder, so a re-run by someone else resolves to them.
        Assert.Equal(AskerValues.Department, plan.Plan.Filters!.Conditions[0].Values[0]);

        Assert.Equal("asker_value_missing", Assert.Throws<DomainException>(() => new SqlServerCompiler().Compile(Plan(mine))).Message);
    }

    [Fact]
    public void Aggregates_CastBitColumns_SoSqlServerAcceptsSumAvgMinMax_AndOtherTypesStayUntouched()
    {
        // "SLA breach rate" style questions: AVG over a bit column is rejected by SQL Server unless it is cast.
        const string rates = """
            {"objective":"x","source":{"schema":"dbo","object":"Orders","alias":"o"},"joins":[],
             "select":[{"alias":"status","column":"o.Status","aggregate":null,"dateTrunc":null},{"alias":"paidRate","column":"o.IsPaid","aggregate":"avg","dateTrunc":null},{"alias":"paid","column":"o.IsPaid","aggregate":"sum","dateTrunc":null},
                       {"alias":"anyPaid","column":"o.IsPaid","aggregate":"max","dateTrunc":null},{"alias":"allPaid","column":"o.IsPaid","aggregate":"min","dateTrunc":null},{"alias":"amount","column":"o.Amount","aggregate":"avg","dateTrunc":null},{"alias":"paidOrders","column":"o.IsPaid","aggregate":"count","dateTrunc":null}],
             "filters":null,"groupBy":["status"],"orderBy":[],"limit":null,"visualization":null,"assumptions":[]}
            """;
        var sqlServer = new SqlServerCompiler().Compile(Plan(rates)).Sql;
        Assert.StartsWith("SELECT TOP (101) [o].[Status] AS [status], AVG(CAST([o].[IsPaid] AS FLOAT)) AS [paidRate], SUM(CAST([o].[IsPaid] AS INT)) AS [paid], MAX(CAST([o].[IsPaid] AS INT)) AS [anyPaid], MIN(CAST([o].[IsPaid] AS INT)) AS [allPaid], AVG([o].[Amount]) AS [amount], COUNT([o].[IsPaid]) AS [paidOrders] FROM", sqlServer, StringComparison.Ordinal);
        var oracle = new OracleCompiler().Compile(Plan(rates)).Sql;
        Assert.Contains("AVG(CAST(\"o\".\"IsPaid\" AS FLOAT)) AS \"paidRate\", SUM(CAST(\"o\".\"IsPaid\" AS INT)) AS \"paid\"", oracle, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlServer_AveragesWholeNumberColumnsAsFloat_AndTotalsThemAsBigint_SoResultsAreNotCutOrOverflowed()
    {
        // T-SQL: AVG over int 2, 3, 3 is 2, and SUM over int overflows above 2,147,483,647.
        const string plan = """
            {"objective":"x","source":{"schema":"dbo","object":"Orders","alias":"o"},"joins":[],
             "select":[{"alias":"avgCustomer","column":"o.CustomerId","aggregate":"avg","dateTrunc":null},{"alias":"sumCustomer","column":"o.CustomerId","aggregate":"sum","dateTrunc":null},
                       {"alias":"avgAmount","column":"o.Amount","aggregate":"avg","dateTrunc":null},{"alias":"sumAmount","column":"o.Amount","aggregate":"sum","dateTrunc":null},{"alias":"maxCustomer","column":"o.CustomerId","aggregate":"max","dateTrunc":null}],
             "filters":null,"groupBy":[],"having":[{"column":"avgCustomer","operator":"gt","values":["2.5"],"compareTo":null}],"orderBy":[],"limit":null,"visualization":null,"assumptions":[]}
            """;
        var sqlServer = new SqlServerCompiler().Compile(Plan(plan)).Sql;
        Assert.StartsWith("SELECT TOP (101) AVG(CAST([o].[CustomerId] AS FLOAT)) AS [avgCustomer], SUM(CAST([o].[CustomerId] AS BIGINT)) AS [sumCustomer], AVG([o].[Amount]) AS [avgAmount], SUM([o].[Amount]) AS [sumAmount], MAX([o].[CustomerId]) AS [maxCustomer] FROM", sqlServer, StringComparison.Ordinal);
        Assert.EndsWith("HAVING AVG(CAST([o].[CustomerId] AS FLOAT)) > @p0", sqlServer, StringComparison.Ordinal);

        // Oracle NUMBER arithmetic keeps the decimals: no cast.
        Assert.Contains("AVG(\"o\".\"CustomerId\") AS \"avgCustomer\", SUM(\"o\".\"CustomerId\") AS \"sumCustomer\"", new OracleCompiler().Compile(Plan(plan)).Sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("eq", "([o].[OrderDate] >= @p0 AND [o].[OrderDate] < @p1)")]
    [InlineData("neq", "([o].[OrderDate] < @p0 OR [o].[OrderDate] >= @p1)")]
    [InlineData("lte", "[o].[OrderDate] < @p0")]
    [InlineData("gt", "[o].[OrderDate] >= @p0")]
    [InlineData("gte", "[o].[OrderDate] >= @p0")]
    [InlineData("lt", "[o].[OrderDate] < @p0")]
    public void DateOnlyValues_OnColumnsThatStoreATime_CoverTheWholeDay(string op, string expected)
    {
        // "Up to 31 December" must include 31 December 10:00: a plain <= would stop at midnight.
        var compiled = new SqlServerCompiler().Compile(Plan(Dated("o.OrderDate", op, "2025-12-31")));
        Assert.EndsWith("WHERE " + expected, compiled.Sql, StringComparison.Ordinal);
        var next = new DateTime(2026, 1, 1);
        var day = new DateTime(2025, 12, 31);
        Assert.Equal(op switch { "eq" or "neq" => [day, next], "lte" or "gt" => [next], _ => new object[] { day } }, compiled.Parameters.Select(p => p.Value!).ToArray());
    }

    [Fact]
    public void DateOnlyValues_InBetweenAndIn_CoverWholeDays_AndValuesWithATimeOrDateColumnsStayExact()
    {
        var between = new SqlServerCompiler().Compile(Plan(Dated("o.OrderDate", "between", "2025-12-01", "2025-12-31")));
        Assert.EndsWith("WHERE ([o].[OrderDate] >= @p0 AND [o].[OrderDate] < @p1)", between.Sql, StringComparison.Ordinal);
        Assert.Equal([new DateTime(2025, 12, 1), new DateTime(2026, 1, 1)], between.Parameters.Select(p => p.Value!).ToArray());

        var days = new SqlServerCompiler().Compile(Plan(Dated("o.OrderDate", "in", "2025-12-24", "2025-12-25")));
        Assert.EndsWith("WHERE (([o].[OrderDate] >= @p0 AND [o].[OrderDate] < @p1) OR ([o].[OrderDate] >= @p2 AND [o].[OrderDate] < @p3))", days.Sql, StringComparison.Ordinal);
        Assert.StartsWith("NOT ((", new SqlServerCompiler().Compile(Plan(Dated("o.OrderDate", "not_in", "2025-12-24"))).Sql.Split("WHERE ")[1], StringComparison.Ordinal);

        // A value with a time is a moment, not a day.
        var moment = new SqlServerCompiler().Compile(Plan(Dated("o.OrderDate", "lte", "2025-12-31T18:00:00")));
        Assert.EndsWith("WHERE [o].[OrderDate] <= @p0", moment.Sql, StringComparison.Ordinal);
        Assert.Equal(new DateTime(2025, 12, 31, 18, 0, 0), moment.Parameters[0].Value);

        // A SQL Server date column has no time: the comparison stays as written. An Oracle DATE stores a time.
        var dateColumns = new Dictionary<string, string>(OrderColumns, StringComparer.OrdinalIgnoreCase) { ["OrderDate"] = "date" };
        var onDateColumn = Plan(Dated("o.OrderDate", "lte", "2025-12-31")) with { Objects = [new AuthorizedObject("o", Guid.NewGuid(), "dbo", "Orders", [], dateColumns)] };
        Assert.EndsWith("WHERE [o].[OrderDate] <= @p0", new SqlServerCompiler().Compile(onDateColumn).Sql, StringComparison.Ordinal);
        Assert.Contains("WHERE \"o\".\"OrderDate\" < :p0", new OracleCompiler().Compile(onDateColumn).Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void RelativeDates_AreBoundToTheDateOfTheRun_AndRejectedOnColumnsThatAreNotDates()
    {
        // "This month": the stored plan keeps the placeholders, so every run covers the month it runs in.
        var json = Dated("o.OrderDate", "gte", "$month_start").Replace("\"groups\":[]", "\"groups\":[{\"logic\":\"and\",\"conditions\":[{\"column\":\"o.OrderDate\",\"operator\":\"lte\",\"values\":[\"$today\"],\"compareTo\":null}],\"groups\":[]}]", StringComparison.Ordinal);
        var plan = Plan(json) with { AskerValues = new Dictionary<string, string> { ["$month_start"] = "2026-11-01", ["$today"] = "2026-11-10" } };
        var compiled = new SqlServerCompiler().Compile(plan);
        Assert.EndsWith("WHERE [o].[OrderDate] >= @p0 AND ([o].[OrderDate] < @p1)", compiled.Sql, StringComparison.Ordinal);
        Assert.Equal([new DateTime(2026, 11, 1), new DateTime(2026, 11, 11)], compiled.Parameters.Select(p => p.Value!).ToArray());
        Assert.Equal("$month_start", plan.Plan.Filters!.Conditions[0].Values[0]);

        Assert.Equal("asker_value_missing", Assert.Throws<DomainException>(() => new SqlServerCompiler().Compile(Plan(json))).Message);
        var onText = Plan(Dated("o.Status", "eq", "$today")) with { AskerValues = new Dictionary<string, string> { ["$today"] = "2026-11-10" } };
        Assert.Equal("plan_filter_value_type_mismatch", Assert.Throws<DomainException>(() => new SqlServerCompiler().Compile(onText)).Message);
    }

    private static string Dated(string column, string op, params string[] values) => $$"""
        {"objective":"x","source":{"schema":"dbo","object":"Orders","alias":"o"},"joins":[],
         "select":[{"alias":"orders","column":null,"aggregate":"count","dateTrunc":null}],
         "filters":{"logic":"and","conditions":[{"column":"{{column}}","operator":"{{op}}","values":[{{string.Join(",", values.Select(v => "\"" + v + "\""))}}],"compareTo":null}],"groups":[]},
         "groupBy":[],"having":[],"orderBy":[],"limit":null,"visualization":null,"assumptions":[]}
        """;

    [Fact]
    public void Compile_RejectsIdentifiersOutsideTheAuthorizedShape_AndTypeMismatches()
    {
        var unknown = Aggregated.Replace("\"c.Name\"", "\"c.Secret\"", StringComparison.Ordinal);
        Assert.Equal("column_not_permitted", Assert.Throws<DomainException>(() => new SqlServerCompiler().Compile(Plan(unknown))).Message);

        var badDate = Aggregated.Replace("\"2026-01-01\"", "\"yesterday\"", StringComparison.Ordinal);
        Assert.Equal("plan_filter_value_type_mismatch", Assert.Throws<DomainException>(() => new SqlServerCompiler().Compile(Plan(badDate))).Message);

        // Quoting neutralizes bracket injection in identifiers that somehow reached the compiler.
        var plan = Plan(Aggregated);
        var hostile = plan with { Objects = [new AuthorizedObject("o", Guid.NewGuid(), "dbo", "Orders]; DROP TABLE x--", [], OrderColumns), plan.Objects[1]] };
        Assert.Equal("compiled_sql_rejected", Assert.Throws<DomainException>(() => new SqlServerCompiler().Compile(hostile)).Message);
    }

    [Fact]
    public void Compile_TypesRowPredicates_AndRejectsMappingsTheContextCannotBind()
    {
        var typed = new SqlServerCompiler().Compile(Plan(Aggregated, [new RowPredicate("CustomerId", "@rs0", "42")]));
        Assert.Equal(42L, typed.Parameters[0].Value);

        var plan = Plan(Aggregated, [new RowPredicate("DeptKey", "@rs0", "FIN")]);
        var guidMapped = plan with { Objects = [plan.Objects[0] with { ColumnTypes = new Dictionary<string, string>(OrderColumns, StringComparer.OrdinalIgnoreCase) { ["DeptKey"] = "uniqueidentifier" } }, plan.Objects[1]] };
        Assert.Equal("row_mapping_type_mismatch", Assert.Throws<DomainException>(() => new SqlServerCompiler().Compile(guidMapped)).Message);
    }

    [Fact]
    public void Factory_MapsPlatformInsightsToSqlServer()
    {
        var factory = new SqlDialectCompilerFactory();
        Assert.IsType<SqlServerCompiler>(factory.Resolve(DatabaseType.PlatformInsights));
        Assert.IsType<OracleCompiler>(factory.Resolve(DatabaseType.Oracle));
    }

    [Fact]
    public void ResultCalculator_ComputesTotalsAndSeriesDeterministically()
    {
        var columns = new[] { new ResultColumn("status", "nvarchar", false, false), new ResultColumn("tickets", "int", true, false) };
        var rows = new List<IReadOnlyList<object?>> { new object?[] { "New", 3 }, new object?[] { "Read", 6 }, new object?[] { "Started", 1 } };
        var (kpis, series) = ResultCalculator.Calculate(columns, rows, new PlanVisualization { Type = "bar", X = "status", Y = ["tickets"] });
        Assert.Equal(10, kpis.Single(k => k.Column == "tickets" && k.Kind == "sum").Value);
        Assert.Equal(6, kpis.Single(k => k.Column == "tickets" && k.Kind == "max").Value);
        var s = Assert.Single(series);
        Assert.Equal(["New", "Read", "Started"], s.Categories);
        Assert.Equal([3, 6, 1], s.Values);
    }

    [Fact]
    public void ResultCalculator_PivotsASecondCategoryColumnIntoOneSeriesPerValue()
    {
        var columns = new[] { new ResultColumn("month", "datetime2", false, true), new ResultColumn("direction", "nvarchar", false, false), new ResultColumn("flights", "int", true, false) };
        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { new DateTime(2025, 1, 1), "Arrival", 10 }, new object?[] { new DateTime(2025, 1, 1), "Departure", 11 },
            new object?[] { new DateTime(2025, 2, 1), "Arrival", 20 }, new object?[] { new DateTime(2025, 2, 1), "Departure", 21 },
            new object?[] { new DateTime(2025, 3, 1), "Departure", 31 },
        };
        var (_, series) = ResultCalculator.Calculate(columns, rows, new PlanVisualization { Type = "line", X = "month", Y = ["flights"] });
        Assert.Equal(2, series.Count);
        Assert.Equal("Arrival", series[0].Name);
        Assert.Equal(["2025-01-01", "2025-02-01", "2025-03-01"], series[0].Categories);
        Assert.Equal([10, 20, 0], series[0].Values);
        Assert.Equal([11, 21, 31], series[1].Values);

        // Too many distinct values (a ranking) is still one series per measure.
        var ranking = Enumerable.Range(0, 20).Select(i => (IReadOnlyList<object?>)new object?[] { new DateTime(2025, 1, 1).AddMonths(i), $"City{i}", i }).ToList();
        var (_, flat) = ResultCalculator.Calculate(columns, ranking, new PlanVisualization { Type = "bar", X = "direction", Y = ["flights"] });
        Assert.Equal("flights", Assert.Single(flat).Name);
    }
}
