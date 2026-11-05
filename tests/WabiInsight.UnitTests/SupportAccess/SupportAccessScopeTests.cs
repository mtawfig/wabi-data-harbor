using WabiInsight.Core.Entities.Support;
using WabiInsight.Core.SupportAccess;

namespace WabiInsight.UnitTests.SupportAccess;

public sealed class SupportAccessScopeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserA = Guid.NewGuid();

    private static List<SupportOperationEntry> Allowed() =>
    [
        new(SupportOperations.ResetOrganizationUserPassword, [UserA.ToString()]),
        new(SupportOperations.CreateOrganizationUser, []),
        new(SupportOperations.ConfigureRole, ["new"]),
        new(SupportOperations.RefreshMetadata, [Guid.NewGuid().ToString()]),
    ];

    [Fact]
    public void Validate_AllowsOnlyGrantedOperationsAndTargets()
    {
        Assert.Null(SupportAccessScope.Validate(Allowed(), SupportOperations.ResetOrganizationUserPassword, UserA.ToString()));
        Assert.Null(SupportAccessScope.Validate(Allowed(), SupportOperations.ResetOrganizationUserPassword, UserA.ToString().ToUpperInvariant()));
        Assert.Equal("support_target_not_granted", SupportAccessScope.Validate(Allowed(), SupportOperations.ResetOrganizationUserPassword, Guid.NewGuid().ToString()));
        Assert.Equal("support_target_required", SupportAccessScope.Validate(Allowed(), SupportOperations.ResetOrganizationUserPassword, null));
        Assert.Null(SupportAccessScope.Validate(Allowed(), SupportOperations.CreateOrganizationUser, null));
        Assert.Null(SupportAccessScope.Validate(Allowed(), SupportOperations.ConfigureRole, "NEW"));
        Assert.Equal("support_operation_not_granted", SupportAccessScope.Validate(Allowed(), SupportOperations.UpdateOrganizationSettings, null));
        Assert.Equal("support_operation_unknown", SupportAccessScope.Validate(Allowed(), "DropDatabase", null));
        Assert.Equal("support_operation_not_available", SupportAccessScope.Validate(Allowed(), SupportOperations.RefreshMetadata, Allowed()[3].TargetReferences[0]));
    }

    [Fact]
    public void ValidateProposal_RequiresTargetsWhereTheCatalogSaysSo()
    {
        Assert.Contains("support_operations_required", SupportAccessScope.ValidateProposal([], null));
        Assert.Contains("support_target_required", SupportAccessScope.ValidateProposal([new(SupportOperations.ConfigureRole, [])], null));
        Assert.Contains("support_target_not_applicable", SupportAccessScope.ValidateProposal([new(SupportOperations.CreateOrganizationUser, ["x"])], null));
        Assert.Contains("support_operations_duplicate", SupportAccessScope.ValidateProposal([new(SupportOperations.CreateOrganizationUser, []), new(SupportOperations.CreateOrganizationUser, [])], null));
        Assert.Contains("support_expiry_out_of_range", SupportAccessScope.ValidateProposal([new(SupportOperations.CreateOrganizationUser, [])], 100));
        Assert.Empty(SupportAccessScope.ValidateProposal([new(SupportOperations.CreateOrganizationUser, []), new(SupportOperations.ConfigureRole, ["new"])], 8));
    }

    [Fact]
    public void Lifecycle_UsableOnlyWhenApprovedOrActiveAndUnexpired()
    {
        Assert.True(SupportAccessScope.IsUsable(SupportAccessGrantStatus.Approved, Now.AddHours(1), Now));
        Assert.True(SupportAccessScope.IsUsable(SupportAccessGrantStatus.Active, Now.AddMinutes(1), Now));
        Assert.False(SupportAccessScope.IsUsable(SupportAccessGrantStatus.Proposed, Now.AddHours(1), Now));
        Assert.False(SupportAccessScope.IsUsable(SupportAccessGrantStatus.Approved, Now.AddMinutes(-1), Now));
        Assert.False(SupportAccessScope.IsUsable(SupportAccessGrantStatus.Revoked, Now.AddHours(1), Now));
        Assert.True(SupportAccessScope.HasExpired(SupportAccessGrantStatus.Active, Now.AddMinutes(-1), Now));
        Assert.False(SupportAccessScope.HasExpired(SupportAccessGrantStatus.Completed, Now.AddMinutes(-1), Now));
    }

    [Fact]
    public void Catalog_HasNoGlobalGrant_AndMarksLaterPhaseOperationsNotExecutable()
    {
        Assert.Equal(7, SupportOperations.Catalog.Count);
        Assert.DoesNotContain(SupportOperations.Catalog, o => o.Code.Contains("All", StringComparison.Ordinal) || o.Code.Contains("Admin", StringComparison.Ordinal));
        Assert.All(SupportOperations.Catalog.Where(o => o.Code is SupportOperations.UpdateTablePermissions or SupportOperations.RefreshMetadata or SupportOperations.DiagnoseApplicationConnection), o => Assert.False(o.Executable));
    }
}
