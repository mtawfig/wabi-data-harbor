using WabiInsight.Core.Entities.Permissions;
using WabiInsight.Core.Permissions;

namespace WabiInsight.UnitTests.Permissions;

public sealed class PermissionCombinerTests
{
    private static readonly Guid C1 = Guid.NewGuid();
    private static readonly Guid C2 = Guid.NewGuid();
    private static readonly Guid C3 = Guid.NewGuid();
    private static readonly HashSet<Guid> Approved = [C1, C2, C3];

    [Fact]
    public void Combine_UnionsColumnsAcrossRoles_AndIgnoresUnapprovedColumns()
    {
        var unapproved = Guid.NewGuid();
        var result = PermissionCombiner.Combine(
        [
            new RoleObjectGrant(Guid.NewGuid(), false, new HashSet<Guid> { C1, unapproved }, RowScope.Self),
            new RoleObjectGrant(Guid.NewGuid(), false, new HashSet<Guid> { C2 }, RowScope.Self),
        ], Approved, new MappingAvailability(true, false, false));

        Assert.Equal(new HashSet<Guid> { C1, C2 }, result.ColumnIds);
        Assert.Equal([RowScope.Self], result.RowScopes);
        Assert.True(result.YieldsRows);
        Assert.False(result.AllRows);
    }

    [Fact]
    public void Combine_AllColumnsGrant_ExpandsToEveryApprovedColumn()
    {
        var result = PermissionCombiner.Combine([new RoleObjectGrant(Guid.NewGuid(), true, new HashSet<Guid>(), RowScope.All)], Approved, MappingAvailability.None);
        Assert.Equal(Approved, result.ColumnIds);
        Assert.True(result.AllRows);
        Assert.True(result.YieldsRows);
    }

    [Fact]
    public void Combine_AllScopeDominates_AndUnmappedScopesYieldNoRows()
    {
        var mixed = PermissionCombiner.Combine(
        [
            new RoleObjectGrant(Guid.NewGuid(), true, new HashSet<Guid>(), RowScope.Department),
            new RoleObjectGrant(Guid.NewGuid(), false, new HashSet<Guid> { C1 }, RowScope.All),
        ], Approved, MappingAvailability.None);
        Assert.True(mixed.AllRows);
        Assert.Empty(mixed.UnmappedScopes);

        var unmapped = PermissionCombiner.Combine([new RoleObjectGrant(Guid.NewGuid(), true, new HashSet<Guid>(), RowScope.Department)], Approved, new MappingAvailability(true, false, true));
        Assert.Empty(unmapped.RowScopes);
        Assert.Equal([RowScope.Department], unmapped.UnmappedScopes);
        Assert.False(unmapped.YieldsRows);

        var missingScope = PermissionCombiner.Combine([new RoleObjectGrant(Guid.NewGuid(), true, new HashSet<Guid>(), null)], Approved, new MappingAvailability(true, true, true));
        Assert.False(missingScope.YieldsRows);
        Assert.Equal(Approved, missingScope.ColumnIds);
    }

    [Fact]
    public void Combine_NoGrants_YieldsNothing()
    {
        var result = PermissionCombiner.Combine([], Approved, MappingAvailability.None);
        Assert.Empty(result.ColumnIds);
        Assert.False(result.YieldsRows);
    }
}
