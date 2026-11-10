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
        new(SupportOperations.UpdateTablePermissions, [Guid.NewGuid().ToString()]),
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
        var allowed = Allowed();
        Assert.Null(SupportAccessScope.Validate(allowed, SupportOperations.UpdateTablePermissions, allowed[3].TargetReferences[0]));
        Assert.Equal("support_target_not_granted", SupportAccessScope.Validate(allowed, SupportOperations.UpdateTablePermissions, Guid.NewGuid().ToString()));
    }

    [Fact]
    public void ValidateProposal_RequiresTargetsWhereTheCatalogSaysSo()
    {
        Assert.Contains("support_operations_required", SupportAccessScope.ValidateProposal([], null));
        Assert.Contains("support_target_required", SupportAccessScope.ValidateProposal([new(SupportOperations.ConfigureRole, [])], null));
        Assert.Contains("support_target_not_applicable", SupportAccessScope.ValidateProposal([new(SupportOperations.UpdateOrganizationSettings, ["x"])], null));
        // Roles a created user may be given are optional targets: none listed means no role may be assigned.
        // The address of the user to create is not optional (P13.26): the organization approves who gets an account.
        const string Address = "email:new.user@org.test";
        Assert.Empty(SupportAccessScope.ValidateProposal([new(SupportOperations.CreateOrganizationUser, [Guid.NewGuid().ToString(), Address])], null));
        Assert.Empty(SupportAccessScope.ValidateProposal([new(SupportOperations.CreateOrganizationUser, [Address])], null));
        Assert.Contains("support_new_user_email_required", SupportAccessScope.ValidateProposal([new(SupportOperations.CreateOrganizationUser, [])], null));
        Assert.Contains("support_new_user_email_required", SupportAccessScope.ValidateProposal([new(SupportOperations.CreateOrganizationUser, [Guid.NewGuid().ToString()])], null));
        Assert.Contains("support_new_user_email_invalid", SupportAccessScope.ValidateProposal([new(SupportOperations.CreateOrganizationUser, ["email:not an address"])], null));
        Assert.Contains("support_new_user_email_invalid", SupportAccessScope.ValidateProposal([new(SupportOperations.CreateOrganizationUser, [.. Enumerable.Range(0, SupportAccessScope.MaxNewUserEmails + 1).Select(i => $"email:u{i}@org.test")])], null));
        Assert.Contains("support_target_not_applicable", SupportAccessScope.ValidateProposal([new(SupportOperations.ConfigureRole, ["new", Address])], null));
        Assert.Contains("support_operations_duplicate", SupportAccessScope.ValidateProposal([new(SupportOperations.CreateOrganizationUser, [Address]), new(SupportOperations.CreateOrganizationUser, [Address])], null));
        Assert.Contains("support_expiry_out_of_range", SupportAccessScope.ValidateProposal([new(SupportOperations.CreateOrganizationUser, [Address])], 100));
        Assert.Empty(SupportAccessScope.ValidateProposal([new(SupportOperations.CreateOrganizationUser, [Address]), new(SupportOperations.ConfigureRole, ["new"])], 8));
    }

    [Fact]
    public void NewUserAddresses_AreReadFromTheGrant_AndComparedAsSignInAddresses()
    {
        var entry = new SupportOperationEntry(SupportOperations.CreateOrganizationUser, [Guid.NewGuid().ToString(), "email:Sara@Org.Test", " EMAIL: omar@org.test "]);
        Assert.Equal(["Sara@Org.Test", "omar@org.test"], SupportAccessScope.EmailsOf(entry));
        Assert.Equal("email:sara@org.test", SupportAccessScope.EmailReference(" Sara@Org.Test "));
        Assert.Null(SupportAccessScope.EmailOf(Guid.NewGuid().ToString()));
        Assert.Null(SupportAccessScope.EmailOf("new"));

        var granted = SupportAccessScope.EmailsOf(entry);
        Assert.True(SupportAccessScope.IsEmailGranted(granted, "sara@org.test"));
        Assert.True(SupportAccessScope.IsEmailGranted(granted, " SARA@ORG.TEST "));
        Assert.False(SupportAccessScope.IsEmailGranted(granted, "sara@org.test.evil.example"));
        Assert.False(SupportAccessScope.IsEmailGranted(granted, "sara+1@org.test"));
        Assert.False(SupportAccessScope.IsEmailGranted(granted, null));
        Assert.False(SupportAccessScope.IsEmailGranted(granted, ""));
        // A grant that names no address allows none.
        Assert.False(SupportAccessScope.IsEmailGranted([], "sara@org.test"));
    }

    [Fact]
    public void CreateOrganizationUser_TakesItsAssignableRolesFromTheGrant()
    {
        var definition = SupportOperations.Find(SupportOperations.CreateOrganizationUser)!;
        Assert.Equal(SupportTargetType.AssignableRole, definition.TargetType);
        Assert.False(definition.RequiresTarget);
        Assert.True(SupportOperations.Find(SupportOperations.UpdateTablePermissions)!.RequiresTarget);
        Assert.False(SupportOperations.Find(SupportOperations.UpdateOrganizationSettings)!.RequiresTarget);

        // The call names no target; the roles it asks for are compared with the roles the grant lists.
        Assert.Null(SupportAccessScope.Validate([new(SupportOperations.CreateOrganizationUser, [Guid.NewGuid().ToString()])], SupportOperations.CreateOrganizationUser, null));
        Assert.Empty(SupportAccessScope.RolesOutsideGrant(["Analyst", "Viewer"], ["Analyst"]));
        Assert.Empty(SupportAccessScope.RolesOutsideGrant(["Analyst"], []));
        Assert.Empty(SupportAccessScope.RolesOutsideGrant([], []));
        Assert.Equal(["Management"], SupportAccessScope.RolesOutsideGrant(["Analyst"], ["Analyst", "Management"]));
        Assert.Equal(["OrganizationAdmin", "Management"], SupportAccessScope.RolesOutsideGrant(["Analyst"], ["OrganizationAdmin", "Management", "management"]));
        // A grant without role targets approves no role at all.
        Assert.Equal(["Analyst"], SupportAccessScope.RolesOutsideGrant([], ["Analyst"]));
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
    public void Catalog_HasNoGlobalGrant_AndEveryOperationIsExecutable()
    {
        Assert.Equal(7, SupportOperations.Catalog.Count);
        Assert.DoesNotContain(SupportOperations.Catalog, o => o.Code.Contains("All", StringComparison.Ordinal) || o.Code.Contains("Admin", StringComparison.Ordinal));
        Assert.All(SupportOperations.Catalog, o => Assert.True(o.Executable));
        Assert.True(SupportOperations.Find(SupportOperations.RefreshMetadata)!.Executable);
        Assert.True(SupportOperations.Find(SupportOperations.DiagnoseApplicationConnection)!.Executable);
    }
}
