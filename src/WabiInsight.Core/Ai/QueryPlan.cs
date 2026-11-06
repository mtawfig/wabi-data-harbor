using System.Text.Json.Serialization;

namespace WabiInsight.Core.Ai;

/// <summary>
/// The only thing the model is allowed to produce (specification §19 step 7): a typed, declarative plan.
/// No SQL, no expressions, no free text that reaches the database. Column references are "alias.Column".
/// </summary>
public sealed class QueryPlan
{
    /// <summary>Panel title when the plan is one query of a multi-query answer (dashboard); empty for a single answer.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

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

    /// <summary>Building blocks shared by the single-query and the analysis (multi-query) schemas.</summary>
    public const string Defs = """
        {
            "objectRef": { "type": "object", "additionalProperties": false, "required": ["schema", "object", "alias"], "properties": { "schema": { "type": "string" }, "object": { "type": "string" }, "alias": { "type": "string" } } },
            "join": { "type": "object", "additionalProperties": false, "required": ["schema", "object", "alias", "type", "on"], "properties": { "schema": { "type": "string" }, "object": { "type": "string" }, "alias": { "type": "string" }, "type": { "type": "string", "enum": ["inner", "left"] }, "on": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["left", "right"], "properties": { "left": { "type": "string" }, "right": { "type": "string" } } } } } },
            "selectItem": { "type": "object", "additionalProperties": false, "required": ["alias", "column", "aggregate", "dateTrunc"], "properties": { "alias": { "type": "string" }, "column": { "anyOf": [ { "type": "string" }, { "type": "null" } ], "description": "alias.Column" }, "aggregate": { "anyOf": [ { "type": "string", "enum": ["count", "count_distinct", "sum", "avg", "min", "max"] }, { "type": "null" } ] }, "dateTrunc": { "anyOf": [ { "type": "string", "enum": ["day", "week", "month", "quarter", "year"] }, { "type": "null" } ] } } },
            "condition": { "type": "object", "additionalProperties": false, "required": ["column", "operator", "values"], "properties": { "column": { "type": "string" }, "operator": { "type": "string", "enum": ["eq", "neq", "gt", "gte", "lt", "lte", "in", "not_in", "between", "contains", "starts_with", "is_null", "not_null"] }, "values": { "type": "array", "items": { "type": "string" } } } },
            "filterGroup": { "type": "object", "additionalProperties": false, "required": ["logic", "conditions", "groups"], "properties": { "logic": { "type": "string", "enum": ["and", "or"] }, "conditions": { "type": "array", "items": { "$ref": "#/$defs/condition" } }, "groups": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["logic", "conditions", "groups"], "properties": { "logic": { "type": "string", "enum": ["and", "or"] }, "conditions": { "type": "array", "items": { "$ref": "#/$defs/condition" } }, "groups": { "type": "array", "items": { "type": "object", "additionalProperties": false, "properties": {} } } } } } } }
          }
        """;

    public const string Json = JsonHead + Defs + "\n        }";

    private const string JsonHead = """
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
          "$defs": 
        """;
}

/// <summary>
/// What the planner actually returns: one objective answered by one to <see cref="AnalysisPlanSchema.MaxQueries"/>
/// independent query plans. A simple question yields one query; a dashboard-style request yields KPI, chart
/// and table queries that are each validated, authorized, compiled and executed on their own.
/// </summary>
public sealed class AnalysisPlan
{
    [JsonPropertyName("objective")]
    public string Objective { get; set; } = string.Empty;

    [JsonPropertyName("queries")]
    public List<QueryPlan> Queries { get; set; } = [];

    [JsonPropertyName("assumptions")]
    public List<string> Assumptions { get; set; } = [];

    /// <summary>"report" (a written study rendered to PDF) and/or "email" (a draft introducing the findings); empty for a plain answer.</summary>
    [JsonPropertyName("deliverables")]
    public List<string> Deliverables { get; set; } = [];

    public bool WantsReport => Deliverables.Contains("report", StringComparer.OrdinalIgnoreCase);

    public bool WantsEmail => Deliverables.Contains("email", StringComparer.OrdinalIgnoreCase);
}

/// <summary>Structured-output schema for the whole answer: the query definition of <see cref="QueryPlanSchema"/> inside a "queries" array.</summary>
public static class AnalysisPlanSchema
{
    public const string Name = "wabi_analysis_plan";

    public const int MaxQueries = 10;

    /// <summary>A dashboard of eight strict-schema queries needs about 6,000 output tokens; the planning call never gets less than this, whatever the provider row says.</summary>
    public const int MinPlanOutputTokens = 8192;

    public const string Json = JsonHead + QueryPlanSchema.Defs + "\n        }";

    private const string JsonHead = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["objective", "queries", "assumptions", "deliverables"],
          "properties": {
            "objective": { "type": "string", "description": "One sentence restating what is answered, in the user's language." },
            "queries": {
              "type": "array",
              "description": "One query for a simple question. For a dashboard, overview or report: several queries, each one panel (kpi for a headline number, bar/line/pie for a chart, table for a breakdown). Never more than ten.",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["title", "source", "joins", "select", "filters", "groupBy", "orderBy", "limit", "visualization"],
                "properties": {
                  "title": { "type": "string", "description": "Short panel title in the user's language." },
                  "source": { "$ref": "#/$defs/objectRef" },
                  "joins": { "type": "array", "items": { "$ref": "#/$defs/join" } },
                  "select": { "type": "array", "items": { "$ref": "#/$defs/selectItem" } },
                  "filters": { "anyOf": [ { "$ref": "#/$defs/filterGroup" }, { "type": "null" } ] },
                  "groupBy": { "type": "array", "items": { "type": "string" }, "description": "Aliases of non-aggregate select items." },
                  "orderBy": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["alias", "direction"], "properties": { "alias": { "type": "string" }, "direction": { "type": "string", "enum": ["asc", "desc"] } } } },
                  "limit": { "anyOf": [ { "type": "integer" }, { "type": "null" } ] },
                  "visualization": { "anyOf": [ { "type": "object", "additionalProperties": false, "required": ["type", "x", "y"], "properties": { "type": { "type": "string", "enum": ["table", "bar", "line", "pie", "kpi"] }, "x": { "anyOf": [ { "type": "string" }, { "type": "null" } ] }, "y": { "type": "array", "items": { "type": "string" } } } }, { "type": "null" } ] }
                }
              }
            },
            "assumptions": { "type": "array", "items": { "type": "string" }, "description": "Interpretation choices worth telling the user, in the user's language." },
            "deliverables": { "type": "array", "items": { "type": "string", "enum": ["report", "email"] }, "description": "report when the user asks for a study, report, document or PDF; email when they ask for an email or message draft; otherwise empty." }
          },
          "$defs": 
        """;
}
