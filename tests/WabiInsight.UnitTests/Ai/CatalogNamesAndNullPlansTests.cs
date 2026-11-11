using System.Text.Json;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Ai.Compilers;

namespace WabiInsight.UnitTests.Ai;

/// <summary>P13.29: names with spaces or Arabic letters can be queried (A3); a plan with JSON nulls is checked, not a crash (E3).</summary>
public sealed class CatalogNamesAndNullPlansTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string ArabicPlan = """
        {"objective":"x","source":{"schema":"sales","object":"طلبات العملاء","alias":"o"},
         "select":[{"alias":"branch","column":"o.اسم الفرع"},{"alias":"total","column":"o.Order Amount","aggregate":"sum"}],
         "filters":{"logic":"and","conditions":[{"column":"o.Order Date","operator":"gte","values":["2026-01-01"]}],"groups":[]},
         "groupBy":["branch"],"orderBy":[{"alias":"total","direction":"desc"}],"limit":10}
        """;

    private static AuthorizedPlan Authorized(QueryPlan plan) => new(plan, Guid.NewGuid(),
    [
        new AuthorizedObject("o", Guid.NewGuid(), "sales", "طلبات العملاء", [new RowPredicate("Branch]Code", "@rs0", "DXB")],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["اسم الفرع"] = "nvarchar", ["Order Amount"] = "decimal", ["Order Date"] = "date", ["Branch]Code"] = "nvarchar" }),
    ], 100);

    [Fact]
    public void Validator_AcceptsCatalogueNamesWithSpacesAndArabicLetters()
    {
        var plan = JsonSerializer.Deserialize<QueryPlan>(ArabicPlan, Json)!;
        Assert.Empty(QueryPlanValidator.Validate(plan));
    }

    [Theory]
    [InlineData("o.Order\u0000Date")]
    [InlineData("o.Order‏Date")]
    [InlineData("o.")]
    public void Validator_StillRejectsControlCharactersAndEmptyNames(string column)
    {
        var plan = JsonSerializer.Deserialize<QueryPlan>(ArabicPlan, Json)!;
        plan.Select[0].Column = column;
        Assert.Contains(QueryPlanValidator.Validate(plan), e => e.Code == "plan_column_invalid");
        Assert.Matches(QueryPlanValidator.CatalogName(), "Order Date");
        Assert.DoesNotMatch(QueryPlanValidator.CatalogName(), new string('x', 129));
    }

    [Fact]
    public void Compilers_QuoteSuchNames_AndEscapeTheQuoteCharacter()
    {
        var plan = Authorized(JsonSerializer.Deserialize<QueryPlan>(ArabicPlan, Json)!);
        var sql = new SqlServerCompiler().Compile(plan).Sql;
        Assert.Contains("[sales].[طلبات العملاء]", sql, StringComparison.Ordinal);
        Assert.Contains("[o].[اسم الفرع] AS [branch]", sql, StringComparison.Ordinal);
        Assert.Contains("[o].[Order Amount]", sql, StringComparison.Ordinal);
        Assert.Contains("[Branch]]Code]", sql, StringComparison.Ordinal);
        var oracle = new OracleCompiler().Compile(plan).Sql;
        Assert.Contains("\"sales\".\"طلبات العملاء\"", oracle, StringComparison.Ordinal);
        Assert.Contains("\"o\".\"Order Date\"", oracle, StringComparison.Ordinal);
    }

    [Fact]
    public void NullListsAndNullEntries_AreReadAsEmpty_SoTheValidatorDecides()
    {
        const string withNulls = """
            {"objective":"x","source":{"schema":"sales","object":"Orders","alias":"o"},"joins":null,
             "select":[null,{"alias":"orders","column":null,"aggregate":"count"}],
             "filters":{"logic":"and","conditions":[null,{"column":"o.Status","operator":"is_null","values":null}],"groups":null},
             "groupBy":null,"having":null,"orderBy":[null],"limit":10,"assumptions":null}
            """;
        var plan = JsonSerializer.Deserialize<QueryPlan>(withNulls, Json)!;
        plan.RemoveNullEntries();
        Assert.Empty(plan.Joins);
        Assert.Single(plan.Select);
        Assert.Single(plan.Filters!.Conditions);
        Assert.Empty(plan.Filters.Conditions[0].Values);
        Assert.Empty(plan.OrderBy);
        Assert.Empty(QueryPlanValidator.Validate(plan));

        // A null source or object name is a validation error, not an exception.
        var bad = JsonSerializer.Deserialize<QueryPlan>("""{"objective":"x","source":null,"select":[{"alias":"n","column":null,"aggregate":"count"}]}""", Json)!;
        bad.RemoveNullEntries();
        Assert.Contains(QueryPlanValidator.Validate(bad), e => e.Code == "plan_object_invalid");
    }

    [Fact]
    public void OrderBy_QuotesTheAliasAsTheSelectItemSpellsIt()
    {
        // Oracle compares quoted names exactly: ORDER BY "total" would not find the column "Total" (ORA-00904).
        var plan = JsonSerializer.Deserialize<QueryPlan>(ArabicPlan.Replace("\"alias\":\"total\",\"column\"", "\"alias\":\"Total\",\"column\"", StringComparison.Ordinal), Json)!;
        Assert.Equal("Total", plan.Select[1].Alias);
        Assert.Equal("total", plan.OrderBy[0].Alias);
        Assert.Empty(QueryPlanValidator.Validate(plan));
        Assert.Contains("ORDER BY \"Total\" DESC", new OracleCompiler().Compile(Authorized(plan)).Sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY [Total] DESC", new SqlServerCompiler().Compile(Authorized(plan)).Sql, StringComparison.Ordinal);
    }
}
