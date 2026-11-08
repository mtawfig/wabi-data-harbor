using WabiInsight.Core.Entities.Permissions;
using WabiInsight.Core.Permissions;

namespace WabiInsight.Core.Ai;

/// <summary>Column names of an object's row-scope mapping (names, because predicates compile against column names).</summary>
public sealed record RowMappingColumns(string? EmployeeColumn, string? DepartmentColumn, string? SectionColumn);

/// <summary>
/// One mandatory row predicate: <c>[Column] = @Parameter</c>; several are OR-ed on the same object. <see cref="Scope"/> is the
/// user's row scope it came from (null for system predicates such as the organization filter on Platform Insights), so the
/// answer can say that its rows are limited to what the asker may see.
/// </summary>
public sealed record RowPredicate(string Column, string ParameterName, string Value, RowScope? Scope = null);

/// <summary>
/// An object the plan may touch, with the predicates the compiler must apply at the source-table level. <see cref="Label"/> and
/// <see cref="ColumnLabels"/> carry the catalogue's display names, used only to explain the query to the asker.
/// </summary>
public sealed record AuthorizedObject(string Alias, Guid ObjectId, string Schema, string ObjectName, IReadOnlyList<RowPredicate> RowPredicates, IReadOnlyDictionary<string, string> ColumnTypes, DisplayLabel? Label = null, IReadOnlyDictionary<string, DisplayLabel>? ColumnLabels = null);

/// <summary>
/// A plan cleared for compilation. <see cref="AskerValues"/> maps each <see cref="Ai.AskerValues"/> placeholder the plan uses to
/// the asker's own code; the compiler binds it as a parameter (the plan itself keeps the placeholder).
/// </summary>
public sealed record AuthorizedPlan(QueryPlan Plan, Guid ApplicationId, IReadOnlyList<AuthorizedObject> Objects, int EffectiveLimit, IReadOnlyDictionary<string, string>? AskerValues = null);

/// <summary>
/// Authorization pass (specification §19 steps 9-10). Every object and column the plan references must be in
/// the caller's effective permissions for the chosen application; row scopes become predicates bound to the
/// user's own context. Pure: it receives the resolved permissions and never widens them.
/// </summary>
public static class QueryPlanAuthorizer
{
    public static (AuthorizedPlan? Plan, IReadOnlyList<PlanError> Errors) Authorize(
        QueryPlan plan,
        Guid applicationId,
        EffectivePermissions permissions,
        IReadOnlyDictionary<Guid, RowMappingColumns> mappings,
        PlanLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(mappings);
        limits ??= new PlanLimits();
        var errors = new List<PlanError>();
        var allowed = permissions.Objects.Where(o => o.ApplicationId == applicationId).ToDictionary(o => $"{o.SchemaName}.{o.ObjectName}", StringComparer.OrdinalIgnoreCase);

        var refs = new List<(string Alias, string Schema, string Object)> { (plan.Source.Alias, plan.Source.Schema, plan.Source.ObjectName) };
        refs.AddRange(plan.Joins.Select(j => (j.Alias, j.Schema, j.ObjectName)));

        var objects = new Dictionary<string, (EffectiveObjectAccess Access, AuthorizedObject Authorized)>(StringComparer.OrdinalIgnoreCase);
        var parameterIndex = 0;
        foreach (var (alias, schema, name) in refs)
        {
            if (!allowed.TryGetValue($"{schema}.{name}", out var access))
            {
                errors.Add(new PlanError("object_not_permitted", $"{schema}.{name}"));
                continue;
            }

            var predicates = new List<RowPredicate>();
            if (!access.RowScopes.Contains(RowScope.All))
            {
                var mapping = mappings.GetValueOrDefault(access.ObjectId) ?? new RowMappingColumns(null, null, null);
                foreach (var scope in access.RowScopes)
                {
                    var (column, value) = scope switch
                    {
                        RowScope.Department => (mapping.DepartmentColumn, permissions.Context.DepartmentCode),
                        RowScope.Section => (mapping.SectionColumn, permissions.Context.SectionCode),
                        RowScope.Self => (mapping.EmployeeColumn, permissions.Context.EmployeeNumber),
                        _ => (null, null),
                    };
                    if (column is not null && !string.IsNullOrWhiteSpace(value))
                    {
                        predicates.Add(new RowPredicate(column, $"@rs{parameterIndex++}", value, scope));
                    }
                }

                if (predicates.Count == 0)
                {
                    // Columns are visible but no scope can yield rows for this user (unmapped or missing context).
                    errors.Add(new PlanError("row_access_denied", $"{schema}.{name}"));
                    continue;
                }
            }

            objects[alias] = (access, new AuthorizedObject(alias, access.ObjectId, access.SchemaName, access.ObjectName, predicates, access.Columns.ToDictionary(c => c.ColumnName, c => c.DataType, StringComparer.OrdinalIgnoreCase),
                new DisplayLabel(access.DisplayNameEn, access.DisplayNameAr),
                access.Columns.ToDictionary(c => c.ColumnName, c => new DisplayLabel(c.DisplayNameEn, c.DisplayNameAr), StringComparer.OrdinalIgnoreCase)));
        }

        foreach (var reference in QueryPlanValidator.ReferencedColumns(plan))
        {
            if (!objects.TryGetValue(reference.Alias, out var entry))
            {
                continue; // the object itself was already reported
            }

            if (!entry.Access.Columns.Any(c => string.Equals(c.ColumnName, reference.Column, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add(new PlanError("column_not_permitted", reference.ToString()));
            }
        }

        // "My department", "my section", "my records": the placeholder needs the asker's own code from their profile.
        var askerValues = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var placeholder in Placeholders(plan.Filters).Distinct(StringComparer.Ordinal))
        {
            var value = placeholder switch
            {
                AskerValues.Employee => permissions.Context.EmployeeNumber,
                AskerValues.Department => permissions.Context.DepartmentCode,
                AskerValues.Section => permissions.Context.SectionCode,
                _ => null,
            };
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(new PlanError("asker_value_missing", placeholder));
            }
            else
            {
                askerValues[placeholder] = value;
            }
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        var limit = Math.Min(plan.Limit ?? limits.DefaultRows, limits.MaxRows);
        return (new AuthorizedPlan(plan, applicationId, refs.Select(r => objects[r.Alias].Authorized).ToList(), limit, askerValues.Count == 0 ? null : askerValues), errors);
    }

    private static IEnumerable<string> Placeholders(PlanFilterGroup? group)
    {
        if (group is null)
        {
            yield break;
        }

        foreach (var value in group.Conditions.Where(c => c.CompareTo is null).SelectMany(c => c.Values).Where(AskerValues.IsPlaceholder))
        {
            yield return value;
        }

        foreach (var value in group.Groups.SelectMany(Placeholders))
        {
            yield return value;
        }
    }
}
