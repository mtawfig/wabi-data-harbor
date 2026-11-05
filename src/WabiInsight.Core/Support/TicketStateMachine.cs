using WabiInsight.Core.Entities.Support;

namespace WabiInsight.Core.Support;

/// <summary>Who is acting on a ticket, resolved from the tenant context by the service (never from the request).</summary>
public enum TicketActorRole
{
    None = 0,
    Requester = 1,
    OrganizationAdmin = 2,
    PlatformAdministrator = 3,
}

/// <summary>The status transition table of specification §15.3. Pure and unit-tested; the service applies it.</summary>
public static class TicketStateMachine
{
    public const int DefaultReopenWindowDays = 14;

    /// <summary>Returns null when the transition is allowed, otherwise a stable error code.</summary>
    public static string? Validate(TicketStatus from, TicketStatus to, TicketActorRole actor, bool hasReason, DateTimeOffset? resolvedAt, DateTimeOffset now, int reopenWindowDays = DefaultReopenWindowDays)
    {
        if (actor == TicketActorRole.None)
        {
            return "ticket_forbidden";
        }

        if (from == to)
        {
            return "ticket_status_unchanged";
        }

        var platform = actor == TicketActorRole.PlatformAdministrator;
        var orgSide = actor is TicketActorRole.Requester or TicketActorRole.OrganizationAdmin;

        return (from, to) switch
        {
            // NEW → READ happens only automatically on the first platform view.
            (TicketStatus.New, TicketStatus.Read) => "ticket_transition_automatic_only",

            (TicketStatus.New or TicketStatus.Read, TicketStatus.Started) => platform ? null : "ticket_transition_forbidden",
            (TicketStatus.New or TicketStatus.Read, TicketStatus.Cancelled) => platform || orgSide ? null : "ticket_transition_forbidden",

            (TicketStatus.Started, TicketStatus.PendingUserResponse) => platform ? null : "ticket_transition_forbidden",
            (TicketStatus.PendingUserResponse, TicketStatus.Started) => platform ? null : "ticket_transition_forbidden",

            (TicketStatus.Started or TicketStatus.PendingUserResponse, TicketStatus.Resolved) => platform ? (hasReason ? null : "ticket_resolution_summary_required") : "ticket_transition_forbidden",
            (TicketStatus.Started or TicketStatus.PendingUserResponse, TicketStatus.Cancelled) => platform ? null
                : actor == TicketActorRole.Requester ? (hasReason ? null : "ticket_cancel_reason_required")
                : "ticket_transition_forbidden",

            (TicketStatus.Resolved, TicketStatus.Started) => !(platform || orgSide) ? "ticket_transition_forbidden"
                : resolvedAt is null || now - resolvedAt.Value <= TimeSpan.FromDays(reopenWindowDays) ? null
                : "ticket_reopen_window_expired",

            _ => "ticket_transition_invalid",
        };
    }

    public static bool IsOpen(TicketStatus status) => status is not (TicketStatus.Resolved or TicketStatus.Cancelled);
}
