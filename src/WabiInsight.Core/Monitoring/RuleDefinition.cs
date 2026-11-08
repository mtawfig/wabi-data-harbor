using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Monitoring;

namespace WabiInsight.Core.Monitoring;

/// <summary>The typed body of a Monitoring Rule (specification §35.1). Exactly one of <see cref="Data"/>, <see cref="Access"/> or <see cref="System"/> is set, matching the rule's kind.</summary>
public sealed class RuleDefinition
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [JsonPropertyName("data")]
    public DataRuleDefinition? Data { get; set; }

    [JsonPropertyName("access")]
    public AccessRuleDefinition? Access { get; set; }

    [JsonPropertyName("system")]
    public SystemRuleDefinition? System { get; set; }

    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    public static RuleDefinition Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<RuleDefinition>(json, JsonOptions) ?? new RuleDefinition();
        }
        catch (JsonException)
        {
            return new RuleDefinition();
        }
    }
}

/// <summary>
/// A data rule: one catalogued object, an optional time window on one of its columns, up to three group-by columns,
/// one measure and a condition, optionally compared with the previous window. Compiled to an ordinary query plan.
/// </summary>
public sealed class DataRuleDefinition
{
    [JsonPropertyName("objectId")]
    public Guid ObjectId { get; set; }

    [JsonPropertyName("timeColumnId")]
    public Guid? TimeColumnId { get; set; }

    /// <summary>Length of the window ending now (organization local time), in hours.</summary>
    [JsonPropertyName("windowHours")]
    public int WindowHours { get; set; } = 24;

    [JsonPropertyName("groupByColumnIds")]
    public List<Guid> GroupByColumnIds { get; set; } = [];

    [JsonPropertyName("measure")]
    public RuleMeasure Measure { get; set; } = new();

    [JsonPropertyName("filters")]
    public List<RuleFilter> Filters { get; set; } = [];

    [JsonPropertyName("condition")]
    public RuleCondition Condition { get; set; } = new();

    /// <summary>"none", or "previous_period": the condition applies to the percent change against the window before.</summary>
    [JsonPropertyName("comparison")]
    public string Comparison { get; set; } = "none";

    [JsonPropertyName("maxGroups")]
    public int MaxGroups { get; set; } = 200;
}

public sealed class RuleMeasure
{
    /// <summary>count, count_distinct, sum, avg, min or max.</summary>
    [JsonPropertyName("aggregate")]
    public string Aggregate { get; set; } = "count";

    /// <summary>Null only for count (rows).</summary>
    [JsonPropertyName("columnId")]
    public Guid? ColumnId { get; set; }
}

public sealed class RuleFilter
{
    [JsonPropertyName("columnId")]
    public Guid ColumnId { get; set; }

    [JsonPropertyName("operator")]
    public string Operator { get; set; } = "eq";

    [JsonPropertyName("values")]
    public List<string> Values { get; set; } = [];
}

public sealed class RuleCondition
{
    /// <summary>gt, gte, lt, lte, eq or neq.</summary>
    [JsonPropertyName("operator")]
    public string Operator { get; set; } = "gt";

    [JsonPropertyName("value")]
    public decimal Value { get; set; }

    public bool Matches(decimal measured) => Operator switch
    {
        "gt" => measured > Value,
        "gte" => measured >= Value,
        "lt" => measured < Value,
        "lte" => measured <= Value,
        "eq" => measured == Value,
        "neq" => measured != Value,
        _ => false,
    };
}

/// <summary>An access rule: one signal per user over a window of the access ledger, with an absolute limit or a baseline multiple.</summary>
public sealed class AccessRuleDefinition
{
    /// <summary>analyses, rows, restricted_touches, exports, unauthorized, identifier_filter_values or accesses.</summary>
    [JsonPropertyName("signal")]
    public string Signal { get; set; } = "analyses";

    [JsonPropertyName("windowHours")]
    public int WindowHours { get; set; } = 24;

    /// <summary>Only facts on these objects (empty: every object).</summary>
    [JsonPropertyName("objectIds")]
    public List<Guid> ObjectIds { get; set; } = [];

    /// <summary>Only facts that touched columns of at least this classification.</summary>
    [JsonPropertyName("minClassification")]
    public DataClassification? MinClassification { get; set; }

    /// <summary>Only users holding one of these role codes (empty: every user).</summary>
    [JsonPropertyName("roleCodes")]
    public List<string> RoleCodes { get; set; } = [];

    /// <summary>Only facts outside the organization's working hours.</summary>
    [JsonPropertyName("outsideWorkingHours")]
    public bool OutsideWorkingHours { get; set; }

    /// <summary>Only users whose account is at most this many days old.</summary>
    [JsonPropertyName("newUsersWithinDays")]
    public int? NewUsersWithinDays { get; set; }

    [JsonPropertyName("threshold")]
    public AccessThreshold Threshold { get; set; } = new();
}

public sealed class AccessThreshold
{
    /// <summary>"absolute": fire when the signal exceeds Value. "baseline": fire when it exceeds Value × the user's baseline statistic (per day, scaled to the window).</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "absolute";

    [JsonPropertyName("value")]
    public decimal Value { get; set; } = 1;

    /// <summary>p95 or median (baseline mode).</summary>
    [JsonPropertyName("statistic")]
    public string Statistic { get; set; } = "p95";

    /// <summary>Baselines with fewer active days are not compared against (the user's role baseline is tried instead).</summary>
    [JsonPropertyName("minActiveDays")]
    public int MinActiveDays { get; set; } = 5;

    /// <summary>Baseline mode never fires below this absolute value, so quiet users are not flagged for tiny numbers.</summary>
    [JsonPropertyName("minimum")]
    public decimal Minimum { get; set; } = 1;
}

/// <summary>
/// A system rule (P14.4): one platform signal of the organization over a window, compared with a threshold.
/// Signals carry no business data: counts of failed connections, emails, report deliveries, removed catalogue
/// entries, and the month's AI spend against the organization's budget.
/// </summary>
public sealed class SystemRuleDefinition
{
    /// <summary>connection_failures, email_failures, ai_budget, metadata_drift or report_failures.</summary>
    [JsonPropertyName("signal")]
    public string Signal { get; set; } = "connection_failures";

    /// <summary>Look-back window in hours (ai_budget always uses the current month).</summary>
    [JsonPropertyName("windowHours")]
    public int WindowHours { get; set; } = 24;

    /// <summary>Fire when the count exceeds this value; for ai_budget, when spend reaches this percentage of the monthly budget.</summary>
    [JsonPropertyName("threshold")]
    public decimal Threshold { get; set; }
}

/// <summary>Pure validation of a rule definition against its kind (no database; catalogue checks happen in the service).</summary>
public static class RuleDefinitionValidator
{
    public static readonly string[] ConditionOperators = ["gt", "gte", "lt", "lte", "eq", "neq"];
    public static readonly string[] AccessSignals = ["analyses", "rows", "restricted_touches", "exports", "unauthorized", "identifier_filter_values", "accesses"];
    public static readonly string[] BaselineSignals = ["analyses", "rows", "restricted_touches", "exports", "unauthorized"];
    public static readonly string[] SystemSignals = ["connection_failures", "email_failures", "ai_budget", "metadata_drift", "report_failures"];

    public static IReadOnlyList<string> Validate(MonitoringRuleKind kind, RuleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new List<string>();
        if (kind == MonitoringRuleKind.Data)
        {
            if (definition.Data is not { } d || definition.Access is not null || definition.System is not null)
            {
                return ["rule_definition_kind_mismatch"];
            }

            if (d.ObjectId == Guid.Empty)
            {
                errors.Add("rule_object_required");
            }

            if (d.WindowHours is < 1 or > 24 * 92)
            {
                errors.Add("rule_window_invalid");
            }

            if (d.GroupByColumnIds.Count > 3 || d.GroupByColumnIds.Distinct().Count() != d.GroupByColumnIds.Count)
            {
                errors.Add("rule_group_by_invalid");
            }

            if (!QueryPlanSchema.Aggregates.Contains(d.Measure.Aggregate))
            {
                errors.Add("rule_measure_invalid");
            }
            else if (d.Measure.Aggregate != "count" && d.Measure.ColumnId is null)
            {
                errors.Add("rule_measure_column_required");
            }

            if (d.Filters.Count > 10 || d.Filters.Any(f => f.ColumnId == Guid.Empty || !QueryPlanSchema.Operators.Contains(f.Operator)))
            {
                errors.Add("rule_filter_invalid");
            }

            if (!ConditionOperators.Contains(d.Condition.Operator))
            {
                errors.Add("rule_condition_invalid");
            }

            if (d.Comparison is not ("none" or "previous_period"))
            {
                errors.Add("rule_comparison_invalid");
            }
            else if (d.Comparison == "previous_period" && (d.TimeColumnId is null || d.GroupByColumnIds.Count > 0))
            {
                errors.Add("rule_comparison_needs_time_without_grouping");
            }

            if (d.MaxGroups is < 1 or > 1000)
            {
                errors.Add("rule_max_groups_invalid");
            }
        }
        else if (kind == MonitoringRuleKind.System)
        {
            if (definition.System is not { } sys || definition.Data is not null || definition.Access is not null)
            {
                return ["rule_definition_kind_mismatch"];
            }

            if (!SystemSignals.Contains(sys.Signal))
            {
                errors.Add("rule_signal_invalid");
            }

            if (sys.WindowHours is < 1 or > 24 * 31)
            {
                errors.Add("rule_window_invalid");
            }

            if (sys.Threshold < 0 || (sys.Signal == "ai_budget" && sys.Threshold is <= 0 or > 1000))
            {
                errors.Add("rule_threshold_invalid");
            }
        }
        else
        {
            if (definition.Access is not { } a || definition.Data is not null || definition.System is not null)
            {
                return ["rule_definition_kind_mismatch"];
            }

            if (!AccessSignals.Contains(a.Signal))
            {
                errors.Add("rule_signal_invalid");
            }

            if (a.WindowHours is < 1 or > 24 * 30)
            {
                errors.Add("rule_window_invalid");
            }

            if (a.NewUsersWithinDays is < 1 or > 365)
            {
                errors.Add("rule_new_users_invalid");
            }

            if (a.Threshold.Mode is not ("absolute" or "baseline"))
            {
                errors.Add("rule_threshold_invalid");
            }
            else if (a.Threshold.Mode == "baseline" && (!BaselineSignals.Contains(a.Signal) || a.Threshold.Statistic is not ("p95" or "median") || a.Threshold.MinActiveDays is < 1 or > 28))
            {
                errors.Add("rule_baseline_invalid");
            }

            if (a.Threshold.Value <= 0 || a.Threshold.Minimum < 0)
            {
                errors.Add("rule_threshold_invalid");
            }
        }

        return errors;
    }
}

/// <summary>Catalogue facts a data rule needs to become a query plan; names come from the organization's metadata.</summary>
public sealed record RuleObjectInfo(Guid ObjectId, Guid ApplicationId, string Schema, string ObjectName, IReadOnlyDictionary<Guid, (string Name, string DataType)> Columns);

/// <summary>The plan(s) a data rule runs: one, or two (current then previous window) for a period comparison.</summary>
public sealed record DataRulePlans(IReadOnlyList<QueryPlan> Plans, IReadOnlyList<string> GroupAliases, string MeasureAlias);

/// <summary>
/// Pure translation of a data rule into the existing query-plan grammar (specification §19), so the rule goes
/// through the same validator, authorizer, dialect compiler and read-only executor as a user's question. The
/// condition is applied to the returned groups by the evaluator; the plan never contains anything the grammar
/// does not allow.
/// </summary>
public static class DataRulePlanBuilder
{
    public const string Alias = "t";
    public const string MeasureAlias = "measure";

    public static DataRulePlans Build(DataRuleDefinition rule, RuleObjectInfo target, DateTime nowLocal)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(target);
        string Column(Guid id) => target.Columns.TryGetValue(id, out var c) ? c.Name : throw new ArgumentException("rule_column_not_on_object", nameof(rule));

        var groupAliases = rule.GroupByColumnIds.Select((_, i) => $"g{i}").ToList();
        var descending = rule.Condition.Operator is "gt" or "gte" or "neq" or "eq";

        QueryPlan PlanFor(DateTime? from, DateTime? to)
        {
            var conditions = rule.Filters.Select(f => new PlanCondition { Column = $"{Alias}.{Column(f.ColumnId)}", Operator = f.Operator, Values = [.. f.Values] }).ToList();
            if (rule.TimeColumnId is { } timeColumn)
            {
                var time = $"{Alias}.{Column(timeColumn)}";
                // A date-only column cannot hold a clock time: windows start and end on whole days.
                var dateOnly = string.Equals(target.Columns[timeColumn].DataType, "date", StringComparison.OrdinalIgnoreCase);
                if (from is { } f)
                {
                    conditions.Add(new PlanCondition { Column = time, Operator = "gte", Values = [Format(dateOnly ? f.Date : f)] });
                }

                if (to is { } t)
                {
                    conditions.Add(new PlanCondition { Column = time, Operator = "lt", Values = [Format(dateOnly ? t.Date : t)] });
                }
            }

            var select = rule.GroupByColumnIds.Select((id, i) => new PlanSelectItem { Alias = groupAliases[i], Column = $"{Alias}.{Column(id)}" }).ToList();
            select.Add(new PlanSelectItem { Alias = MeasureAlias, Aggregate = rule.Measure.Aggregate, Column = rule.Measure.ColumnId is { } m ? $"{Alias}.{Column(m)}" : null });
            return new QueryPlan
            {
                Title = "Monitoring rule",
                Objective = "Monitoring rule",
                Source = new PlanObjectRef { Schema = target.Schema, ObjectName = target.ObjectName, Alias = Alias },
                Select = select,
                Filters = conditions.Count == 0 ? null : new PlanFilterGroup { Logic = "and", Conditions = conditions },
                GroupBy = [.. groupAliases],
                OrderBy = groupAliases.Count == 0 ? [] : [new PlanOrderItem { Alias = MeasureAlias, Direction = descending ? "desc" : "asc" }],
                Limit = groupAliases.Count == 0 ? 1 : rule.MaxGroups,
                Visualization = new PlanVisualization { Type = "table" },
            };
        }

        var window = TimeSpan.FromHours(rule.WindowHours);
        if (rule.TimeColumnId is null)
        {
            return new DataRulePlans([PlanFor(null, null)], groupAliases, MeasureAlias);
        }

        var current = PlanFor(nowLocal - window, null);
        return rule.Comparison == "previous_period"
            ? new DataRulePlans([current, PlanFor(nowLocal - window - window, nowLocal - window)], groupAliases, MeasureAlias)
            : new DataRulePlans([current], groupAliases, MeasureAlias);
    }

    private static string Format(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
