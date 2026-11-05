using System.Text.Json.Serialization;

namespace WabiInsight.Core.Ai;

/// <summary>
/// The only thing the model is allowed to produce (specification §19 step 7): a typed, declarative plan.
/// No SQL, no expressions, no free text that reaches the database. Column references are "alias.Column".
/// </summary>
public sealed class QueryPlan
{
    [JsonPropertyName("objective")]
    public string Objective { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public PlanObjectRef Source { get; set; } = new();

    [JsonPropertyName("joins")]
    public List<PlanJoin> Joins { get; set; } = [];

    [JsonPropertyName("select")]
    public List<PlanSelectItem> Select { get; set; } = [];

    [JsonPropertyName("filters")]
    public PlanFilterGroup? Filters { get; set; }

    [JsonPropertyName("groupBy")]
    public List<string> GroupBy { get; set; } = [];

    [JsonPropertyName("orderBy")]
    public List<PlanOrderItem> OrderBy { get; set; } = [];

    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    [JsonPropertyName("visualization")]
    public PlanVisualization? Visualization { get; set; }

    [JsonPropertyName("assumptions")]
    public List<string> Assumptions { get; set; } = [];
}

public sealed class PlanObjectRef
{
    [JsonPropertyName("schema")]
    public string Schema { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string ObjectName { get; set; } = string.Empty;

    [JsonPropertyName("alias")]
    public string Alias { get; set; } = string.Empty;
}

public sealed class PlanJoin
{
    [JsonPropertyName("schema")]
    public string Schema { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string ObjectName { get; set; } = string.Empty;

    [JsonPropertyName("alias")]
    public string Alias { get; set; } = string.Empty;

    /// <summary>inner or left.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "inner";

    [JsonPropertyName("on")]
    public List<PlanJoinCondition> On { get; set; } = [];
}

public sealed class PlanJoinCondition
{
    [JsonPropertyName("left")]
    public string Left { get; set; } = string.Empty;

    [JsonPropertyName("right")]
    public string Right { get; set; } = string.Empty;
}

/// <summary>Exactly one of column (plain), aggregate (+ optional column) or dateTrunc (+ column).</summary>
public sealed class PlanSelectItem
{
    [JsonPropertyName("alias")]
    public string Alias { get; set; } = string.Empty;

    [JsonPropertyName("column")]
    public string? Column { get; set; }

    /// <summary>count, count_distinct, sum, avg, min, max.</summary>
    [JsonPropertyName("aggregate")]
    public string? Aggregate { get; set; }

    /// <summary>day, week, month, quarter, year.</summary>
    [JsonPropertyName("dateTrunc")]
    public string? DateTrunc { get; set; }
}

public sealed class PlanFilterGroup
{
    /// <summary>and / or.</summary>
    [JsonPropertyName("logic")]
    public string Logic { get; set; } = "and";

    [JsonPropertyName("conditions")]
    public List<PlanCondition> Conditions { get; set; } = [];

    [JsonPropertyName("groups")]
    public List<PlanFilterGroup> Groups { get; set; } = [];
}

public sealed class PlanCondition
{
    [JsonPropertyName("column")]
    public string Column { get; set; } = string.Empty;

    /// <summary>eq, neq, gt, gte, lt, lte, in, not_in, between, contains, starts_with, is_null, not_null.</summary>
    [JsonPropertyName("operator")]
    public string Operator { get; set; } = string.Empty;

    [JsonPropertyName("values")]
    public List<string> Values { get; set; } = [];
}

public sealed class PlanOrderItem
{
    [JsonPropertyName("alias")]
    public string Alias { get; set; } = string.Empty;

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "asc";
}

public sealed class PlanVisualization
{
    /// <summary>table, bar, line, pie, kpi.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "table";

    [JsonPropertyName("x")]
    public string? X { get; set; }

    [JsonPropertyName("y")]
    public List<string> Y { get; set; } = [];
}

/// <summary>JSON schema sent as the structured-output format. Size and range limits are deliberately absent (providers reject them); the validator enforces them.</summary>
public static class QueryPlanSchema
{
    public const string Name = "wabi_query_plan";

    public static readonly string[] Aggregates = ["count", "count_distinct", "sum", "avg", "min", "max"];
    public static readonly string[] DateTruncUnits = ["day", "week", "month", "quarter", "year"];
    public static readonly string[] Operators = ["eq", "neq", "gt", "gte", "lt", "lte", "in", "not_in", "between", "contains", "starts_with", "is_null", "not_null"];
    public static readonly string[] JoinTypes = ["inner", "left"];
    public static readonly string[] VisualizationTypes = ["table", "bar", "line", "pie", "kpi"];

    public const string Json = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["objective", "source", "joins", "select", "filters", "groupBy", "orderBy", "limit", "visualization", "assumptions"],
          "properties": {
            "objective": { "type": "string", "description": "One sentence restating what is computed, in the user's language." },
            "source": { "$ref": "#/$defs/objectRef" },
            "joins": { "type": "array", "items": { "$ref": "#/$defs/join" } },
            "select": { "type": "array", "items": { "$ref": "#/$defs/selectItem" } },
            "filters": { "anyOf": [ { "$ref": "#/$defs/filterGroup" }, { "type": "null" } ] },
            "groupBy": { "type": "array", "items": { "type": "string" }, "description": "Aliases of non-aggregate select items." },
            "orderBy": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["alias", "direction"], "properties": { "alias": { "type": "string" }, "direction": { "type": "string", "enum": ["asc", "desc"] } } } },
            "limit": { "anyOf": [ { "type": "integer" }, { "type": "null" } ] },
            "visualization": { "anyOf": [ { "type": "object", "additionalProperties": false, "required": ["type", "x", "y"], "properties": { "type": { "type": "string", "enum": ["table", "bar", "line", "pie", "kpi"] }, "x": { "anyOf": [ { "type": "string" }, { "type": "null" } ] }, "y": { "type": "array", "items": { "type": "string" } } } }, { "type": "null" } ] },
            "assumptions": { "type": "array", "items": { "type": "string" }, "description": "Interpretation choices worth telling the user, in the user's language." }
          },
          "$defs": {
            "objectRef": { "type": "object", "additionalProperties": false, "required": ["schema", "object", "alias"], "properties": { "schema": { "type": "string" }, "object": { "type": "string" }, "alias": { "type": "string" } } },
            "join": { "type": "object", "additionalProperties": false, "required": ["schema", "object", "alias", "type", "on"], "properties": { "schema": { "type": "string" }, "object": { "type": "string" }, "alias": { "type": "string" }, "type": { "type": "string", "enum": ["inner", "left"] }, "on": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["left", "right"], "properties": { "left": { "type": "string" }, "right": { "type": "string" } } } } } },
            "selectItem": { "type": "object", "additionalProperties": false, "required": ["alias", "column", "aggregate", "dateTrunc"], "properties": { "alias": { "type": "string" }, "column": { "anyOf": [ { "type": "string" }, { "type": "null" } ], "description": "alias.Column" }, "aggregate": { "anyOf": [ { "type": "string", "enum": ["count", "count_distinct", "sum", "avg", "min", "max"] }, { "type": "null" } ] }, "dateTrunc": { "anyOf": [ { "type": "string", "enum": ["day", "week", "month", "quarter", "year"] }, { "type": "null" } ] } } },
            "condition": { "type": "object", "additionalProperties": false, "required": ["column", "operator", "values"], "properties": { "column": { "type": "string" }, "operator": { "type": "string", "enum": ["eq", "neq", "gt", "gte", "lt", "lte", "in", "not_in", "between", "contains", "starts_with", "is_null", "not_null"] }, "values": { "type": "array", "items": { "type": "string" } } } },
            "filterGroup": { "type": "object", "additionalProperties": false, "required": ["logic", "conditions", "groups"], "properties": { "logic": { "type": "string", "enum": ["and", "or"] }, "conditions": { "type": "array", "items": { "$ref": "#/$defs/condition" } }, "groups": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["logic", "conditions", "groups"], "properties": { "logic": { "type": "string", "enum": ["and", "or"] }, "conditions": { "type": "array", "items": { "$ref": "#/$defs/condition" } }, "groups": { "type": "array", "items": { "type": "object", "additionalProperties": false, "properties": {} } } } } } } }
          }
        }
        """;
}
