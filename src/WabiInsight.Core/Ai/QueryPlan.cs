using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WabiInsight.Core.Ai;

/// <summary>
/// The only thing the model is allowed to produce (specification §19 step 7): a typed, declarative plan.
/// No SQL, no expressions, no free text that reaches the database. Column references are "alias.Column".
/// </summary>
public sealed class QueryPlan
{
    private List<PlanCondition> _having = [];

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

    /// <summary>
    /// Conditions on aggregated values (SQL HAVING), all of which must hold: each condition's column is the alias of an aggregate
    /// select item, and <see cref="PlanCondition.CompareTo"/> may name another aggregate alias. Empty when no total is filtered.
    /// </summary>
    [JsonPropertyName("having")]
    public List<PlanCondition> Having { get => _having; set => _having = value ?? []; }

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

/// <summary>
/// Exactly one of column (plain), aggregate (+ optional column) or dateTrunc (+ column). The model writes an empty string for
/// "none" (plain strings keep the structured-output grammar small); it is read as null, so stored plans with nulls read the same.
/// </summary>
public sealed class PlanSelectItem
{
    private string? _column;
    private string? _aggregate;
    private string? _dateTrunc;

    [JsonPropertyName("alias")]
    public string Alias { get; set; } = string.Empty;

    [JsonPropertyName("column")]
    public string? Column { get => _column; set => _column = PlanText.NullIfEmpty(value); }

    /// <summary>count, count_distinct, sum, avg, min, max.</summary>
    [JsonPropertyName("aggregate")]
    public string? Aggregate { get => _aggregate; set => _aggregate = PlanText.NullIfEmpty(value); }

    /// <summary>day, week, month, quarter, year.</summary>
    [JsonPropertyName("dateTrunc")]
    public string? DateTrunc { get => _dateTrunc; set => _dateTrunc = PlanText.NullIfEmpty(value); }
}

internal static class PlanText
{
    public static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
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
    private string? _compareTo;

    [JsonPropertyName("column")]
    public string Column { get; set; } = string.Empty;

    /// <summary>eq, neq, gt, gte, lt, lte, in, not_in, between, contains, starts_with, is_null, not_null.</summary>
    [JsonPropertyName("operator")]
    public string Operator { get; set; } = string.Empty;

    /// <summary>
    /// Literal values, an <see cref="AskerValues"/> placeholder the server replaces with the asker's own code after
    /// authorization (the model never sees the codes), or a <see cref="RelativeDates"/> placeholder resolved to a date at each run.
    /// </summary>
    [JsonPropertyName("values")]
    public List<string> Values { get; set; } = [];

    /// <summary>
    /// Compares <see cref="Column"/> with another column instead of values ("alias.Column" in filters, an aggregate alias
    /// in having); only eq, neq, gt, gte, lt and lte, and <see cref="Values"/> stays empty. The model writes an empty string
    /// for a literal comparison; it is read as null.
    /// </summary>
    [JsonPropertyName("compareTo")]
    public string? CompareTo { get => _compareTo; set => _compareTo = PlanText.NullIfEmpty(value); }
}

/// <summary>
/// Filter values that stand for the asker's own codes, the same codes row scopes use (employee number, department code,
/// section code from the user's organization profile). The planner writes the placeholder; the authorizer checks that the
/// asker has the value and the compiler binds it as a parameter, so a stored plan re-run by someone else (a periodic report
/// recipient, a history re-run) resolves to that person.
/// </summary>
public static class AskerValues
{
    public const string Prefix = "$asker.";
    public const string Employee = "$asker.employee";
    public const string Department = "$asker.department";
    public const string Section = "$asker.section";

    public static readonly string[] All = [Employee, Department, Section];

    /// <summary>Operators a placeholder may be used with (it stands for one code).</summary>
    public static readonly string[] Operators = ["eq", "neq", "in", "not_in"];

    public static bool IsPlaceholder(string? value) => value is not null && value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Filter values that stand for a date relative to today: <c>$today</c>, <c>$week_start</c> (Monday), <c>$month_start</c>,
/// <c>$quarter_start</c> or <c>$year_start</c>, optionally followed by + or - a whole number of days (d), weeks (w),
/// months (m), quarters (q) or years (y), for example <c>$today-7d</c> or <c>$month_start-1m</c>. The planner writes them for
/// "this month", "the last 30 days" and similar; the authorizer resolves them to a date every time the plan runs, so a
/// re-run, a saved analysis or a periodic report always covers the current period instead of the dates of the first ask.
/// </summary>
public static partial class RelativeDates
{
    /// <summary>Operators a relative date may be used with (it stands for one day).</summary>
    public static readonly string[] Operators = ["eq", "neq", "gt", "gte", "lt", "lte", "between"];

    private static readonly string[] Anchors = ["$today", "$week_start", "$month_start", "$quarter_start", "$year_start"];

    [GeneratedRegex(@"^\$(today|week_start|month_start|quarter_start|year_start)(?:([+-])(\d{1,4})([dwmqy]))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool IsPlaceholder(string? value) => value is not null && Anchors.Any(a => value.StartsWith(a, StringComparison.Ordinal));

    public static bool IsValid(string value) => Pattern().IsMatch(value);

    /// <summary>The date the placeholder stands for, as ISO 8601 (YYYY-MM-DD); null when it is not a valid placeholder.</summary>
    public static string? Resolve(string value, DateOnly today)
    {
        var match = value is null ? null : Pattern().Match(value);
        if (match is null || !match.Success)
        {
            return null;
        }

        var date = match.Groups[1].Value switch
        {
            "week_start" => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)),
            "month_start" => new DateOnly(today.Year, today.Month, 1),
            "quarter_start" => new DateOnly(today.Year, ((today.Month - 1) / 3 * 3) + 1, 1),
            "year_start" => new DateOnly(today.Year, 1, 1),
            _ => today,
        };
        if (match.Groups[3].Success)
        {
            var amount = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) * (match.Groups[2].Value == "-" ? -1 : 1);
            try
            {
                date = match.Groups[4].Value switch
                {
                    "d" => date.AddDays(amount),
                    "w" => date.AddDays(amount * 7),
                    "m" => date.AddMonths(amount),
                    "q" => date.AddMonths(amount * 3),
                    _ => date.AddYears(amount),
                };
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
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
    /// <summary>Operators of a column-to-column comparison (<see cref="PlanCondition.CompareTo"/>).</summary>
    public static readonly string[] CompareOperators = ["eq", "neq", "gt", "gte", "lt", "lte"];
    /// <summary>Operators of a condition on an aggregated value (<see cref="QueryPlan.Having"/>).</summary>
    public static readonly string[] HavingOperators = ["eq", "neq", "gt", "gte", "lt", "lte", "between"];
    public static readonly string[] JoinTypes = ["inner", "left"];
    public static readonly string[] VisualizationTypes = ["table", "bar", "line", "pie", "kpi"];

    /// <summary>Building blocks shared by the single-query and the analysis (multi-query) schemas.</summary>
    public const string Defs = """
        {
            "objectRef": { "type": "object", "additionalProperties": false, "required": ["schema", "object", "alias"], "properties": { "schema": { "type": "string" }, "object": { "type": "string" }, "alias": { "type": "string" } } },
            "join": { "type": "object", "additionalProperties": false, "required": ["schema", "object", "alias", "type", "on"], "properties": { "schema": { "type": "string" }, "object": { "type": "string" }, "alias": { "type": "string" }, "type": { "type": "string", "enum": ["inner", "left"] }, "on": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["left", "right"], "properties": { "left": { "type": "string" }, "right": { "type": "string" } } } } } },
            "selectItem": { "type": "object", "additionalProperties": false, "required": ["alias", "column", "aggregate", "dateTrunc"], "properties": { "alias": { "type": "string" }, "column": { "type": "string", "description": "alias.Column; empty only for a count of rows." }, "aggregate": { "type": "string", "enum": ["", "count", "count_distinct", "sum", "avg", "min", "max"], "description": "Empty for a plain or dateTrunc column." }, "dateTrunc": { "type": "string", "enum": ["", "day", "week", "month", "quarter", "year"], "description": "Empty unless the column is a date grouped by this unit." } } },
            "condition": { "type": "object", "additionalProperties": false, "required": ["column", "operator", "values", "compareTo"], "properties": { "column": { "type": "string" }, "operator": { "type": "string", "enum": ["eq", "neq", "gt", "gte", "lt", "lte", "in", "not_in", "between", "contains", "starts_with", "is_null", "not_null"] }, "values": { "type": "array", "items": { "type": "string" } }, "compareTo": { "type": "string", "description": "Another column to compare with (values empty); empty for a comparison with values." } } },
            "filterGroup": { "type": "object", "additionalProperties": false, "required": ["logic", "conditions", "groups"], "properties": { "logic": { "type": "string", "enum": ["and", "or"] }, "conditions": { "type": "array", "items": { "$ref": "#/$defs/condition" } }, "groups": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["logic", "conditions", "groups"], "properties": { "logic": { "type": "string", "enum": ["and", "or"] }, "conditions": { "type": "array", "items": { "$ref": "#/$defs/condition" } }, "groups": { "type": "array", "items": { "type": "object", "additionalProperties": false, "properties": {} } } } } } } }
          }
        """;

    public const string Json = JsonHead + Defs + "\n        }";

    private const string JsonHead = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["objective", "source", "joins", "select", "filters", "groupBy", "having", "orderBy", "limit", "visualization", "assumptions"],
          "properties": {
            "objective": { "type": "string", "description": "One sentence restating what is computed, in the user's language." },
            "source": { "$ref": "#/$defs/objectRef" },
            "joins": { "type": "array", "items": { "$ref": "#/$defs/join" } },
            "select": { "type": "array", "items": { "$ref": "#/$defs/selectItem" } },
            "filters": { "anyOf": [ { "$ref": "#/$defs/filterGroup" }, { "type": "null" } ] },
            "groupBy": { "type": "array", "items": { "type": "string" }, "description": "Aliases of non-aggregate select items." },
            "having": { "type": "array", "items": { "$ref": "#/$defs/condition" }, "description": "Conditions on aggregate select aliases (totals, counts), all of which must hold; empty when none." },
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
                "required": ["title", "source", "joins", "select", "filters", "groupBy", "having", "orderBy", "limit", "visualization"],
                "properties": {
                  "title": { "type": "string", "description": "Short panel title in the user's language." },
                  "source": { "$ref": "#/$defs/objectRef" },
                  "joins": { "type": "array", "items": { "$ref": "#/$defs/join" } },
                  "select": { "type": "array", "items": { "$ref": "#/$defs/selectItem" } },
                  "filters": { "anyOf": [ { "$ref": "#/$defs/filterGroup" }, { "type": "null" } ] },
                  "groupBy": { "type": "array", "items": { "type": "string" }, "description": "Aliases of non-aggregate select items." },
                  "having": { "type": "array", "items": { "$ref": "#/$defs/condition" }, "description": "Conditions on aggregate select aliases (totals, counts), all of which must hold; empty when none." },
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
