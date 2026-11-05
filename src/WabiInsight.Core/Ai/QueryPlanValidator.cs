using System.Globalization;
using System.Text.RegularExpressions;

namespace WabiInsight.Core.Ai;

public sealed record PlanError(string Code, string Detail);

/// <summary>Column reference resolved to its alias and bare column name.</summary>
public readonly record struct ColumnRef(string Alias, string Column)
{
    public override string ToString() => $"{Alias}.{Column}";
}

public sealed record PlanLimits(int MaxRows = 1000, int DefaultRows = 100, int MaxJoins = 5, int MaxSelect = 30, int MaxConditions = 20, int MaxValueLength = 200);

/// <summary>
/// Shape validation (specification §19 step 8): identifiers, operators, aggregate functions, join forms,
/// grouping consistency, ordering targets and limits. Pure: no database, no permissions (that is the
/// authorizer's job). Every rejection carries a stable code the UI can localize.
/// </summary>
public static partial class QueryPlanValidator
{
    public static IReadOnlyList<PlanError> Validate(QueryPlan plan, PlanLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        limits ??= new PlanLimits();
        var errors = new List<PlanError>();
        var aliases = new Dictionary<string, (string Schema, string Object)>(StringComparer.OrdinalIgnoreCase);

        void Object(string schema, string name, string alias, string where)
        {
            if (!Identifier().IsMatch(schema ?? string.Empty) || !Identifier().IsMatch(name ?? string.Empty))
            {
                errors.Add(new PlanError("plan_object_invalid", $"{where}: '{schema}.{name}'"));
            }

            if (!Alias().IsMatch(alias ?? string.Empty))
            {
                errors.Add(new PlanError("plan_alias_invalid", $"{where}: '{alias}'"));
            }
            else if (!aliases.TryAdd(alias!, (schema ?? string.Empty, name ?? string.Empty)))
            {
                errors.Add(new PlanError("plan_alias_duplicate", alias!));
            }
        }

        Object(plan.Source.Schema, plan.Source.ObjectName, plan.Source.Alias, "source");
        if (plan.Joins.Count > limits.MaxJoins)
        {
            errors.Add(new PlanError("plan_too_many_joins", plan.Joins.Count.ToString(CultureInfo.InvariantCulture)));
        }

        foreach (var join in plan.Joins)
        {
            Object(join.Schema, join.ObjectName, join.Alias, "join");
            if (!QueryPlanSchema.JoinTypes.Contains(join.Type, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(new PlanError("plan_join_type_invalid", join.Type));
            }

            if (join.On.Count is 0 or > 3)
            {
                errors.Add(new PlanError("plan_join_condition_invalid", join.Alias));
            }

            foreach (var on in join.On)
            {
                var left = ParseColumn(on.Left, aliases, errors, "join");
                var right = ParseColumn(on.Right, aliases, errors, "join");
                if (left is { } l && right is { } r && !string.Equals(l.Alias, join.Alias, StringComparison.OrdinalIgnoreCase) && !string.Equals(r.Alias, join.Alias, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new PlanError("plan_join_condition_invalid", $"{on.Left} = {on.Right} does not reference {join.Alias}"));
                }
            }
        }

        if (plan.Select.Count == 0 || plan.Select.Count > limits.MaxSelect)
        {
            errors.Add(new PlanError("plan_select_invalid", plan.Select.Count.ToString(CultureInfo.InvariantCulture)));
        }

        var selectAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasAggregate = false;
        var plainAliases = new List<string>();
        foreach (var item in plan.Select)
        {
            if (!Alias().IsMatch(item.Alias ?? string.Empty) || !selectAliases.Add(item.Alias!))
            {
                errors.Add(new PlanError("plan_select_alias_invalid", item.Alias ?? string.Empty));
            }

            var kinds = (item.Aggregate is not null ? 1 : 0) + (item.DateTrunc is not null ? 1 : 0);
            if (kinds > 1)
            {
                errors.Add(new PlanError("plan_select_item_invalid", item.Alias ?? string.Empty));
                continue;
            }

            if (item.Aggregate is not null)
            {
                hasAggregate = true;
                if (!QueryPlanSchema.Aggregates.Contains(item.Aggregate, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add(new PlanError("plan_aggregate_invalid", item.Aggregate));
                }

                if (item.Column is null && !string.Equals(item.Aggregate, "count", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new PlanError("plan_aggregate_column_required", item.Alias ?? string.Empty));
                }
                else if (item.Column is not null)
                {
                    ParseColumn(item.Column, aliases, errors, "select");
                }
            }
            else if (item.DateTrunc is not null)
            {
                plainAliases.Add(item.Alias ?? string.Empty);
                if (!QueryPlanSchema.DateTruncUnits.Contains(item.DateTrunc, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add(new PlanError("plan_date_trunc_invalid", item.DateTrunc));
                }

                if (item.Column is null)
                {
                    errors.Add(new PlanError("plan_select_item_invalid", item.Alias ?? string.Empty));
                }
                else
                {
                    ParseColumn(item.Column, aliases, errors, "select");
                }
            }
            else
            {
                plainAliases.Add(item.Alias ?? string.Empty);
                if (item.Column is null)
                {
                    errors.Add(new PlanError("plan_select_item_invalid", item.Alias ?? string.Empty));
                }
                else
                {
                    ParseColumn(item.Column, aliases, errors, "select");
                }
            }
        }

        var conditions = 0;
        if (plan.Filters is not null)
        {
            ValidateGroup(plan.Filters, 0, aliases, limits, errors, ref conditions);
        }

        if (conditions > limits.MaxConditions)
        {
            errors.Add(new PlanError("plan_too_many_conditions", conditions.ToString(CultureInfo.InvariantCulture)));
        }

        foreach (var group in plan.GroupBy)
        {
            if (!selectAliases.Contains(group) || !plainAliases.Contains(group, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(new PlanError("plan_group_by_invalid", group));
            }
        }

        if (hasAggregate)
        {
            foreach (var plain in plainAliases.Where(a => !plan.GroupBy.Contains(a, StringComparer.OrdinalIgnoreCase)))
            {
                errors.Add(new PlanError("plan_group_by_missing", plain));
            }
        }
        else if (plan.GroupBy.Count > 0 && plan.GroupBy.Count != plainAliases.Count)
        {
            errors.Add(new PlanError("plan_group_by_invalid", "grouping without aggregates must cover every selected column"));
        }

        foreach (var order in plan.OrderBy)
        {
            if (!selectAliases.Contains(order.Alias))
            {
                errors.Add(new PlanError("plan_order_by_invalid", order.Alias));
            }

            if (order.Direction is not ("asc" or "desc"))
            {
                errors.Add(new PlanError("plan_order_direction_invalid", order.Direction));
            }
        }

        if (plan.Limit is { } limit && (limit < 1 || limit > limits.MaxRows))
        {
            errors.Add(new PlanError("plan_limit_invalid", limit.ToString(CultureInfo.InvariantCulture)));
        }

        if (plan.Visualization is { } vis)
        {
            if (!QueryPlanSchema.VisualizationTypes.Contains(vis.Type, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(new PlanError("plan_visualization_invalid", vis.Type));
            }

            if (vis.X is not null && !selectAliases.Contains(vis.X))
            {
                errors.Add(new PlanError("plan_visualization_invalid", vis.X));
            }

            foreach (var y in vis.Y.Where(y => !selectAliases.Contains(y)))
            {
                errors.Add(new PlanError("plan_visualization_invalid", y));
            }
        }

        return errors;
    }

    /// <summary>Every column reference in the plan (select, joins, filters), resolved. Call only on a shape-valid plan.</summary>
    public static IReadOnlyList<ColumnRef> ReferencedColumns(QueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var refs = new List<ColumnRef>();
        foreach (var item in plan.Select.Where(i => i.Column is not null))
        {
            refs.Add(Split(item.Column!));
        }

        foreach (var on in plan.Joins.SelectMany(j => j.On))
        {
            refs.Add(Split(on.Left));
            refs.Add(Split(on.Right));
        }

        if (plan.Filters is not null)
        {
            Collect(plan.Filters, refs);
        }

        return refs.Distinct().ToList();
    }

    public static ColumnRef Split(string reference)
    {
        var index = reference.IndexOf('.', StringComparison.Ordinal);
        return index <= 0 ? new ColumnRef(string.Empty, reference) : new ColumnRef(reference[..index], reference[(index + 1)..]);
    }

    private static void Collect(PlanFilterGroup group, List<ColumnRef> refs)
    {
        refs.AddRange(group.Conditions.Select(c => Split(c.Column)));
        foreach (var child in group.Groups)
        {
            Collect(child, refs);
        }
    }

    private static void ValidateGroup(PlanFilterGroup group, int depth, Dictionary<string, (string Schema, string Object)> aliases, PlanLimits limits, List<PlanError> errors, ref int conditions)
    {
        if (depth > 2)
        {
            errors.Add(new PlanError("plan_filter_nesting_invalid", depth.ToString(CultureInfo.InvariantCulture)));
            return;
        }

        if (group.Logic is not ("and" or "or"))
        {
            errors.Add(new PlanError("plan_filter_logic_invalid", group.Logic));
        }

        foreach (var condition in group.Conditions)
        {
            conditions++;
            ParseColumn(condition.Column, aliases, errors, "filter");
            var op = condition.Operator?.ToLowerInvariant() ?? string.Empty;
            if (!QueryPlanSchema.Operators.Contains(op))
            {
                errors.Add(new PlanError("plan_operator_invalid", condition.Operator ?? string.Empty));
                continue;
            }

            var count = condition.Values?.Count ?? 0;
            var valid = op switch
            {
                "is_null" or "not_null" => count == 0,
                "in" or "not_in" => count is >= 1 and <= 50,
                "between" => count == 2,
                _ => count == 1,
            };
            if (!valid)
            {
                errors.Add(new PlanError("plan_filter_values_invalid", $"{condition.Column} {op}"));
            }

            if (condition.Values is not null && condition.Values.Any(v => v is null || v.Length > limits.MaxValueLength))
            {
                errors.Add(new PlanError("plan_filter_value_too_long", condition.Column));
            }
        }

        foreach (var child in group.Groups)
        {
            ValidateGroup(child, depth + 1, aliases, limits, errors, ref conditions);
        }
    }

    private static ColumnRef? ParseColumn(string? reference, Dictionary<string, (string Schema, string Object)> aliases, List<PlanError> errors, string where)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            errors.Add(new PlanError("plan_column_invalid", $"{where}: empty"));
            return null;
        }

        var split = Split(reference);
        if (split.Alias.Length == 0 || !Identifier().IsMatch(split.Column) || !aliases.ContainsKey(split.Alias))
        {
            errors.Add(new PlanError("plan_column_invalid", $"{where}: '{reference}'"));
            return null;
        }

        return split;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_$#]{0,127}$")]
    public static partial Regex Identifier();

    [GeneratedRegex("^[a-z][a-z0-9_]{0,23}$")]
    public static partial Regex Alias();
}
