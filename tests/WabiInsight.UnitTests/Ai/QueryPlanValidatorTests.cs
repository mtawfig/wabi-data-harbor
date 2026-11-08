using System.Text.Json;
using WabiInsight.Core.Ai;

namespace WabiInsight.UnitTests.Ai;

public sealed class QueryPlanValidatorTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static QueryPlan Plan(string json) => JsonSerializer.Deserialize<QueryPlan>(json, Json)!;

    private const string Good = """
        {"objective":"Total amount per customer","source":{"schema":"dbo","object":"Orders","alias":"o"},
         "joins":[{"schema":"dbo","object":"Customers","alias":"c","type":"inner","on":[{"left":"o.CustomerId","right":"c.CustomerId"}]}],
         "select":[{"alias":"customer","column":"c.Name","aggregate":null,"dateTrunc":null},{"alias":"total","column":"o.Amount","aggregate":"sum","dateTrunc":null},{"alias":"month","column":"o.OrderDate","aggregate":null,"dateTrunc":"month"}],
         "filters":{"logic":"and","conditions":[{"column":"o.Status","operator":"in","values":["Paid","Shipped"]},{"column":"o.OrderDate","operator":"gte","values":["2026-01-01"]}],"groups":[]},
         "groupBy":["customer","month"],"orderBy":[{"alias":"total","direction":"desc"}],"limit":50,
         "visualization":{"type":"bar","x":"customer","y":["total"]},"assumptions":[]}
        """;

    [Fact]
    public void ValidateAnalysis_RequiresOneToEightQueries_AndNamesTheFailingQuery()
    {
        Assert.Contains(QueryPlanValidator.ValidateAnalysis(new AnalysisPlan()), e => e.Code == "plan_queries_invalid");
        var tooMany = new AnalysisPlan { Queries = Enumerable.Range(0, AnalysisPlanSchema.MaxQueries + 1).Select(_ => Plan(Good)).ToList() };
        Assert.Contains(QueryPlanValidator.ValidateAnalysis(tooMany), e => e.Code == "plan_too_many_queries");

        var dashboard = new AnalysisPlan { Objective = "Dashboard", Queries = [Plan(Good), Plan(Good.Replace("\"operator\":\"in\"", "\"operator\":\"like\"", StringComparison.Ordinal))] };
        dashboard.Queries[1].Title = new string('x', QueryPlanValidator.MaxTitleLength + 1);
        var errors = QueryPlanValidator.ValidateAnalysis(dashboard);
        Assert.Contains(errors, e => e.Code == "plan_operator_invalid" && e.Detail.StartsWith("query 2:", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Code == "plan_query_title_invalid");
        Assert.DoesNotContain(errors, e => e.Detail.StartsWith("query 1:", StringComparison.Ordinal));
        Assert.Contains("\"queries\"", AnalysisPlanSchema.Json, StringComparison.Ordinal);
        Assert.Contains(QueryPlanSchema.Defs, AnalysisPlanSchema.Json, StringComparison.Ordinal);
        Assert.Contains(QueryPlanSchema.Defs, QueryPlanSchema.Json, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AcceptsAWellFormedPlan_AndListsItsColumns()
    {
        var plan = Plan(Good);
        Assert.Empty(QueryPlanValidator.Validate(plan));
        var columns = QueryPlanValidator.ReferencedColumns(plan).Select(c => c.ToString()).ToList();
        Assert.Contains("c.Name", columns);
        Assert.Contains("o.Status", columns);
        Assert.Contains("c.CustomerId", columns);
    }

    [Theory]
    [InlineData("\"alias\":\"o\"}", "\"alias\":\"o; drop table x\"}", "plan_alias_invalid")]
    [InlineData("\"object\":\"Orders\"", "\"object\":\"Orders]; --\"", "plan_object_invalid")]
    [InlineData("\"operator\":\"in\"", "\"operator\":\"like\"", "plan_operator_invalid")]
    [InlineData("\"aggregate\":\"sum\"", "\"aggregate\":\"exec\"", "plan_aggregate_invalid")]
    [InlineData("\"column\":\"o.Status\"", "\"column\":\"x.Status\"", "plan_column_invalid")]
    [InlineData("\"groupBy\":[\"customer\",\"month\"]", "\"groupBy\":[\"customer\"]", "plan_group_by_missing")]
    [InlineData("\"orderBy\":[{\"alias\":\"total\",\"direction\":\"desc\"}]", "\"orderBy\":[{\"alias\":\"nope\",\"direction\":\"desc\"}]", "plan_order_by_invalid")]
    [InlineData("\"limit\":50", "\"limit\":50000", "plan_limit_invalid")]
    [InlineData("\"values\":[\"2026-01-01\"]", "\"values\":[\"2026-01-01\",\"2026-02-01\"]", "plan_filter_values_invalid")]
    [InlineData("\"type\":\"inner\"", "\"type\":\"cross\"", "plan_join_type_invalid")]
    [InlineData("\"x\":\"customer\"", "\"x\":\"missing\"", "plan_visualization_invalid")]
    public void Validate_RejectsUnsafeOrInconsistentShapes(string find, string replace, string code)
    {
        var plan = Plan(Good.Replace(find, replace, StringComparison.Ordinal));
        Assert.Contains(QueryPlanValidator.Validate(plan), e => e.Code == code);
    }

    [Fact]
    public void Validate_RejectsTooManyJoins_DeepNesting_AndLongValues()
    {
        var plan = Plan(Good);
        for (var i = 0; i < 6; i++)
        {
            plan.Joins.Add(new PlanJoin { Schema = "dbo", ObjectName = "T" + i, Alias = "t" + i, Type = "left", On = [new PlanJoinCondition { Left = "o.Id", Right = $"t{i}.Id" }] });
        }

        plan.Filters!.Groups.Add(new PlanFilterGroup { Logic = "or", Groups = [new PlanFilterGroup { Logic = "and", Groups = [new PlanFilterGroup { Logic = "and" }] }] });
        plan.Filters.Conditions.Add(new PlanCondition { Column = "o.Status", Operator = "eq", Values = [new string('x', 500)] });
        var errors = QueryPlanValidator.Validate(plan);
        Assert.Contains(errors, e => e.Code == "plan_too_many_joins");
        Assert.Contains(errors, e => e.Code == "plan_filter_nesting_invalid");
        Assert.Contains(errors, e => e.Code == "plan_filter_value_too_long");
    }

    [Fact]
    public void Validate_AcceptsHavingOnAggregates_ColumnComparisons_AndAskerPlaceholders()
    {
        var plan = Plan(Good);
        plan.Having = [new PlanCondition { Column = "total", Operator = "gt", Values = ["1000"] }];
        plan.Filters!.Conditions.Add(new PlanCondition { Column = "o.ShippedDate", Operator = "gt", CompareTo = "o.DueDate" });
        plan.Filters.Conditions.Add(new PlanCondition { Column = "c.DeptCode", Operator = "eq", Values = [AskerValues.Department] });
        Assert.Empty(QueryPlanValidator.Validate(plan));

        // The compared column is a referenced column, so the authorizer checks it like any other.
        Assert.Contains("o.DueDate", QueryPlanValidator.ReferencedColumns(plan).Select(c => c.ToString()));
        Assert.Contains("\"having\"", AnalysisPlanSchema.Json, StringComparison.Ordinal);
        Assert.Contains("\"compareTo\"", QueryPlanSchema.Defs, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("having", "customer", "gt", "1", null, "plan_having_invalid")]       // not an aggregate alias
    [InlineData("having", "total", "contains", "1", null, "plan_having_invalid")]    // operator not allowed on totals
    [InlineData("having", "total", "gt", "", null, "plan_filter_values_invalid")]    // no value
    [InlineData("having", "total", "gt", "$asker.department", null, "plan_filter_values_invalid")]
    [InlineData("having", "total", "gt", null, "customer", "plan_having_invalid")]   // compared with a non-aggregate
    [InlineData("filters", "o.OrderDate", "between", null, "o.DueDate", "plan_compare_invalid")]
    [InlineData("filters", "o.OrderDate", "gt", "2026-01-01", "o.DueDate", "plan_compare_invalid")] // values and compareTo
    [InlineData("filters", "o.OrderDate", "gt", null, "x.DueDate", "plan_column_invalid")]
    [InlineData("filters", "o.DeptCode", "contains", "$asker.department", null, "plan_asker_value_invalid")]
    [InlineData("filters", "o.DeptCode", "eq", "$asker.manager", null, "plan_asker_value_invalid")]
    public void Validate_RejectsMalformedHaving_Comparisons_AndPlaceholders(string where, string column, string op, string? value, string? compareTo, string code)
    {
        var plan = Plan(Good);
        var condition = new PlanCondition { Column = column, Operator = op, Values = value is null ? [] : [value], CompareTo = compareTo };
        if (where == "having")
        {
            plan.Having = [condition];
        }
        else
        {
            plan.Filters!.Conditions.Add(condition);
        }

        Assert.Contains(QueryPlanValidator.Validate(plan), e => e.Code == code);
    }

    [Fact]
    public void Validate_RejectsHavingWithoutAggregates()
    {
        var plan = Plan(Good.Replace("\"aggregate\":\"sum\"", "\"aggregate\":null", StringComparison.Ordinal).Replace("\"groupBy\":[\"customer\",\"month\"]", "\"groupBy\":[]", StringComparison.Ordinal));
        plan.Having = [new PlanCondition { Column = "total", Operator = "gt", Values = ["1"] }];
        Assert.Contains(QueryPlanValidator.Validate(plan), e => e.Code == "plan_having_invalid");
    }

    [Fact]
    public void EmptyStrings_FromTheModel_ReadAsNone_SoTheSchemaStaysWithinTheProvidersGrammarLimit()
    {
        // The structured-output schema uses plain strings ("" = none) instead of nullable unions, which providers count heavily.
        var plan = Plan(Good.Replace("{\"alias\":\"customer\",\"column\":\"c.Name\",\"aggregate\":null,\"dateTrunc\":null}", "{\"alias\":\"customer\",\"column\":\"c.Name\",\"aggregate\":\"\",\"dateTrunc\":\"\"}", StringComparison.Ordinal)
            .Replace("\"values\":[\"Paid\",\"Shipped\"]}", "\"values\":[\"Paid\",\"Shipped\"],\"compareTo\":\"\"}", StringComparison.Ordinal));
        Assert.Null(plan.Select[0].Aggregate);
        Assert.Null(plan.Select[0].DateTrunc);
        Assert.Null(plan.Filters!.Conditions[0].CompareTo);
        Assert.Empty(Plan(Good.Replace("\"groupBy\":", "\"having\":null,\"groupBy\":", StringComparison.Ordinal)).Having);
        Assert.Empty(QueryPlanValidator.Validate(plan));
        Assert.DoesNotContain("{ \"type\": \"null\" }", QueryPlanSchema.Defs.Split("\"filterGroup\"")[0], StringComparison.Ordinal);
        Assert.Contains("\"having\": { \"type\": \"array\"", AnalysisPlanSchema.Json, StringComparison.Ordinal);
    }
}
