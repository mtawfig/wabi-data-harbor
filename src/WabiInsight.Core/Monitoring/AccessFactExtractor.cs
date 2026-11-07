using WabiInsight.Core.Ai;

namespace WabiInsight.Core.Monitoring;

/// <summary>One executed (or attempted) query with the rows it returned (0 when it never ran).</summary>
public sealed record PlanExecution(QueryPlan Plan, int RowCount);

/// <summary>What an analysis did to one object, by name; the recorder maps names to catalogue ids.</summary>
public sealed record ObjectAccessSummary(
    string Schema,
    string ObjectName,
    bool IsPrimary,
    int RowCount,
    IReadOnlySet<string> Columns,
    IReadOnlySet<string> FilterColumns,
    IReadOnlyDictionary<string, int> FilterValueCounts);

/// <summary>
/// Pure extraction of access facts from query plans (Phase 14 ledger): every object a plan references, the
/// columns used on it, the filter columns and how many literal values each filter carries. Aliases are resolved
/// to the plan's source and joins; a bare column name belongs to the source.
/// </summary>
public static class AccessFactExtractor
{
    public static IReadOnlyList<ObjectAccessSummary> Summarize(IReadOnlyList<PlanExecution> executions)
    {
        ArgumentNullException.ThrowIfNull(executions);
        var objects = new Dictionary<string, Accumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var (plan, rowCount) in executions)
        {
            var aliases = new Dictionary<string, (string Schema, string Object)>(StringComparer.OrdinalIgnoreCase)
            {
                [plan.Source.Alias ?? string.Empty] = (plan.Source.Schema, plan.Source.ObjectName),
            };
            foreach (var join in plan.Joins)
            {
                aliases[join.Alias ?? string.Empty] = (join.Schema, join.ObjectName);
            }

            var source = Accumulate(objects, plan.Source.Schema, plan.Source.ObjectName);
            source.IsPrimary = true;
            source.RowCount += Math.Max(0, rowCount);
            foreach (var join in plan.Joins)
            {
                Accumulate(objects, join.Schema, join.ObjectName);
            }

            foreach (var reference in QueryPlanValidator.ReferencedColumns(plan))
            {
                var target = Resolve(aliases, reference, plan);
                if (target is not null)
                {
                    Accumulate(objects, target.Value.Schema, target.Value.Object).Columns.Add(reference.Column);
                }
            }

            if (plan.Filters is not null)
            {
                CollectFilters(plan.Filters, aliases, plan, objects);
            }
        }

        return objects.Values
            .Select(a => new ObjectAccessSummary(a.Schema, a.ObjectName, a.IsPrimary, a.RowCount, a.Columns, a.FilterColumns, a.FilterValueCounts))
            .ToList();
    }

    private static void CollectFilters(PlanFilterGroup group, Dictionary<string, (string Schema, string Object)> aliases, QueryPlan plan, Dictionary<string, Accumulator> objects)
    {
        foreach (var condition in group.Conditions)
        {
            var reference = QueryPlanValidator.Split(condition.Column);
            var target = Resolve(aliases, reference, plan);
            if (target is null)
            {
                continue;
            }

            var accumulator = Accumulate(objects, target.Value.Schema, target.Value.Object);
            accumulator.Columns.Add(reference.Column);
            accumulator.FilterColumns.Add(reference.Column);
            var values = condition.Values?.Count ?? 0;
            accumulator.FilterValueCounts[reference.Column] = accumulator.FilterValueCounts.GetValueOrDefault(reference.Column) + values;
        }

        foreach (var child in group.Groups)
        {
            CollectFilters(child, aliases, plan, objects);
        }
    }

    private static (string Schema, string Object)? Resolve(Dictionary<string, (string Schema, string Object)> aliases, ColumnRef reference, QueryPlan plan)
    {
        if (aliases.TryGetValue(reference.Alias, out var target))
        {
            return target;
        }

        return reference.Alias.Length == 0 ? (plan.Source.Schema, plan.Source.ObjectName) : null;
    }

    private static Accumulator Accumulate(Dictionary<string, Accumulator> objects, string schema, string name)
    {
        var key = $"{schema}.{name}";
        if (!objects.TryGetValue(key, out var accumulator))
        {
            accumulator = new Accumulator(schema, name);
            objects[key] = accumulator;
        }

        return accumulator;
    }

    private sealed class Accumulator(string schema, string objectName)
    {
        public string Schema { get; } = schema;

        public string ObjectName { get; } = objectName;

        public bool IsPrimary { get; set; }

        public int RowCount { get; set; }

        public HashSet<string> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> FilterColumns { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> FilterValueCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
