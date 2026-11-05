using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Usage;

/// <summary>
/// Append-only stream of verified platform events; the single source of truth for platform and
/// organization metrics (specification §12.3, §13, B9). Never derived from AI output.
/// </summary>
public class UsageEvent
{
    public Guid UsageEventId { get; set; }

    /// <summary>Null for platform-level events (administrator actions, platform health).</summary>
    public Guid? OrganizationId { get; set; }

    public PrincipalType ActorType { get; set; }

    public Guid? ActorId { get; set; }

    /// <summary>Department of the acting organization user at the time of the event, for department usage metrics.</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>Stable code from <see cref="UsageEventTypes"/>.</summary>
    public string EventType { get; set; } = string.Empty;

    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    /// <summary>Count or measure carried by the event (1 for simple occurrences; tokens, bytes or milliseconds for measured ones).</summary>
    public long Quantity { get; set; } = 1;

    public bool Succeeded { get; set; } = true;

    public string? MetadataJson { get; set; }

    public string? CorrelationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Stable usage event codes. Later phases add AI, report, sharing and support codes.</summary>
public static class UsageEventTypes
{
    public const string Login = "Auth.Login";
    public const string LoginFailed = "Auth.LoginFailed";
    public const string Logout = "Auth.Logout";
    public const string PasswordChanged = "Auth.PasswordChanged";
    public const string OrganizationCreated = "Organization.Created";
    public const string OrganizationStatusChanged = "Organization.StatusChanged";
    public const string UserCreated = "User.Created";
    public const string AdministratorCreated = "Administrator.Created";
    public const string EmailQueued = "Email.Queued";
    public const string EmailSent = "Email.Sent";
    public const string EmailFailed = "Email.Failed";
    public const string ApiError = "Api.Error";
}
