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
    private static readonly Dictionary<string, string> CustomerColumns = new(StringComparer.OrdinalIgnoreCase) { ["CustomerId"] = "int", ["Name"] = "nvarchar" };

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
}
