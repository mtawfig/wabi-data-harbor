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
}
