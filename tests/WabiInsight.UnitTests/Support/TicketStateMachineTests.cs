using WabiInsight.Core.Entities.Support;
using WabiInsight.Core.Support;

namespace WabiInsight.UnitTests.Support;

public sealed class TicketStateMachineTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(TicketStatus.New, TicketStatus.Started, TicketActorRole.PlatformAdministrator, true, null)]
    [InlineData(TicketStatus.Read, TicketStatus.Started, TicketActorRole.PlatformAdministrator, true, null)]
    [InlineData(TicketStatus.New, TicketStatus.Started, TicketActorRole.Requester, true, "ticket_transition_forbidden")]
    [InlineData(TicketStatus.New, TicketStatus.Cancelled, TicketActorRole.Requester, false, null)]
    [InlineData(TicketStatus.Read, TicketStatus.Cancelled, TicketActorRole.OrganizationAdmin, false, null)]
    [InlineData(TicketStatus.New, TicketStatus.Read, TicketActorRole.PlatformAdministrator, true, "ticket_transition_automatic_only")]
    [InlineData(TicketStatus.Started, TicketStatus.PendingUserResponse, TicketActorRole.PlatformAdministrator, false, null)]
    [InlineData(TicketStatus.Started, TicketStatus.PendingUserResponse, TicketActorRole.OrganizationAdmin, false, "ticket_transition_forbidden")]
    [InlineData(TicketStatus.PendingUserResponse, TicketStatus.Started, TicketActorRole.PlatformAdministrator, false, null)]
    [InlineData(TicketStatus.Started, TicketStatus.Resolved, TicketActorRole.PlatformAdministrator, true, null)]
    [InlineData(TicketStatus.Started, TicketStatus.Resolved, TicketActorRole.PlatformAdministrator, false, "ticket_resolution_summary_required")]
    [InlineData(TicketStatus.PendingUserResponse, TicketStatus.Resolved, TicketActorRole.Requester, true, "ticket_transition_forbidden")]
    [InlineData(TicketStatus.Started, TicketStatus.Cancelled, TicketActorRole.PlatformAdministrator, false, null)]
    [InlineData(TicketStatus.Started, TicketStatus.Cancelled, TicketActorRole.Requester, true, null)]
    [InlineData(TicketStatus.Started, TicketStatus.Cancelled, TicketActorRole.Requester, false, "ticket_cancel_reason_required")]
    [InlineData(TicketStatus.Started, TicketStatus.Cancelled, TicketActorRole.OrganizationAdmin, true, "ticket_transition_forbidden")]
    [InlineData(TicketStatus.Resolved, TicketStatus.Cancelled, TicketActorRole.PlatformAdministrator, true, "ticket_transition_invalid")]
    [InlineData(TicketStatus.Cancelled, TicketStatus.Started, TicketActorRole.PlatformAdministrator, true, "ticket_transition_invalid")]
    [InlineData(TicketStatus.Started, TicketStatus.Started, TicketActorRole.PlatformAdministrator, true, "ticket_status_unchanged")]
    [InlineData(TicketStatus.New, TicketStatus.Cancelled, TicketActorRole.None, true, "ticket_forbidden")]
    public void Validate_FollowsTheTransitionTable(TicketStatus from, TicketStatus to, TicketActorRole actor, bool hasReason, string? expected)
    {
        Assert.Equal(expected, TicketStateMachine.Validate(from, to, actor, hasReason, null, Now));
    }

    [Theory]
    [InlineData(TicketActorRole.Requester)]
    [InlineData(TicketActorRole.OrganizationAdmin)]
    [InlineData(TicketActorRole.PlatformAdministrator)]
    public void Reopen_IsAllowedInsideTheWindow_AndRejectedAfterIt(TicketActorRole actor)
    {
        Assert.Null(TicketStateMachine.Validate(TicketStatus.Resolved, TicketStatus.Started, actor, false, Now.AddDays(-13), Now));
        Assert.Equal("ticket_reopen_window_expired", TicketStateMachine.Validate(TicketStatus.Resolved, TicketStatus.Started, actor, false, Now.AddDays(-15), Now));
        Assert.Null(TicketStateMachine.Validate(TicketStatus.Resolved, TicketStatus.Started, actor, false, Now.AddDays(-15), Now, reopenWindowDays: 30));
    }

    [Fact]
    public void IsOpen_IsFalseOnlyForResolvedAndCancelled()
    {
        Assert.True(TicketStateMachine.IsOpen(TicketStatus.New));
        Assert.True(TicketStateMachine.IsOpen(TicketStatus.PendingUserResponse));
        Assert.False(TicketStateMachine.IsOpen(TicketStatus.Resolved));
        Assert.False(TicketStateMachine.IsOpen(TicketStatus.Cancelled));
    }
}
