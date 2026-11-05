using WabiInsight.Core.Entities.Permissions;

namespace WabiInsight.Core.Permissions;

/// <summary>One role's contribution to one object, before combination.</summary>
public sealed record RoleObjectGrant(Guid RoleId, bool AllColumns, IReadOnlySet<Guid> ColumnIds, RowScope? Scope);

/// <summary>Which mapping columns exist on the object (review B13).</summary>
public sealed record MappingAvailability(bool Employee, bool Department, bool Section)
{
    public static readonly MappingAvailability None = new(false, false, false);

    public bool Supports(RowScope scope) => scope switch
    {
        RowScope.All => true,
        RowScope.Department => Department,
        RowScope.Section => Section,
        RowScope.Self => Employee,
        _ => false,
    };
}

/// <summary>Combined result for one object: allowed columns and the row predicates to OR together.</summary>
public sealed record CombinedObjectAccess(IReadOnlySet<Guid> ColumnIds, IReadOnlySet<RowScope> RowScopes, IReadOnlySet<RowScope> UnmappedScopes)
{
    /// <summary>True when at least one scope can produce rows. False means the object is listed but returns no rows.</summary>
    public bool YieldsRows => RowScopes.Count > 0;

    public bool AllRows => RowScopes.Contains(RowScope.All);
}

/// <summary>
/// Pure combination rules (review B12/B13): union of columns across roles, OR of row scopes, <c>All</c> dominates,
/// a scope whose mapping column is absent is dropped (contributes no rows), and nothing here widens the
/// organization boundary, which the caller enforces before calling.
/// </summary>
public static class PermissionCombiner
{
    public static CombinedObjectAccess Combine(IReadOnlyCollection<RoleObjectGrant> grants, IReadOnlySet<Guid> approvedColumnIds, MappingAvailability mapping)
    {
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(approvedColumnIds);
        ArgumentNullException.ThrowIfNull(mapping);

        var columns = new HashSet<Guid>();
        var scopes = new HashSet<RowScope>();
        var unmapped = new HashSet<RowScope>();
        foreach (var grant in grants)
        {
            if (grant.AllColumns)
            {
                columns.UnionWith(approvedColumnIds);
            }
            else
            {
                columns.UnionWith(grant.ColumnIds.Where(approvedColumnIds.Contains));
            }

            if (grant.Scope is { } scope)
            {
                if (mapping.Supports(scope))
                {
                    scopes.Add(scope);
                }
                else
                {
                    unmapped.Add(scope);
                }
            }
        }

        if (scopes.Contains(RowScope.All))
        {
            // All rows dominate: narrower and unmapped scopes no longer matter.
            scopes.Clear();
            scopes.Add(RowScope.All);
            unmapped.Clear();
        }

        return new CombinedObjectAccess(columns, scopes, unmapped);
    }
}
