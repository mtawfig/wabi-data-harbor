using WabiInsight.Core.Common;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Monitoring;

public enum MonitoringRuleKind
{
    /// <summary>Over the organization's business data, through the secure query path, under the owner's permissions.</summary>
    Data = 1,

    /// <summary>Over the access ledger (how users use the platform), compared to limits or baselines.</summary>
    Access = 2,

    /// <summary>Over platform signals of the organization: connection failures, email failures, AI spend, metadata drift, report delivery failures.</summary>
    System = 3,
}

public enum AlertSeverity
{
    Info = 1,
    Medium = 2,
    High = 3,
}

public enum AlertStatus
{
    Open = 1,
    Acknowledged = 2,
    Resolved = 3,
    Dismissed = 4,
}

public enum RuleEvaluationOutcome
{
    NoMatch = 1,
    Fired = 2,
    Failed = 3,
    Skipped = 4,
}

public enum AlertEventType
{
    Raised = 1,
    Repeated = 2,
    Acknowledged = 3,
    Resolved = 4,
    Dismissed = 5,
    Reopened = 6,
    UserSuspended = 7,
}

/// <summary>
/// A Monitoring Rule (specification §35.1): a typed definition bound to the organization's own catalogued objects
/// and columns (data rules) or to the access ledger (access rules), evaluated on a schedule under the
/// <see cref="OwnerUserId"/>'s permissions. Never contains business logic of its own.
/// </summary>
public class MonitoringRule : AuditableEntity, ITenantOwned
{
    public Guid RuleId { get; set; }

    public Guid OrganizationId { get; set; }

    /// <summary>The user whose permissions the rule runs under. Organization Administrators may choose any active user; Management users own their rules.</summary>
    public Guid OwnerUserId { get; set; }

    public Guid CreatedByUserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public MonitoringRuleKind Kind { get; set; }

    /// <summary>The template the rule was created from, when any (the editor reopens it with the template's form).</summary>
    public string? TemplateCode { get; set; }

    /// <summary>Serialized <c>RuleDefinition</c>; validated on every save and every evaluation.</summary>
    public string DefinitionJson { get; set; } = "{}";

    public AlertSeverity Severity { get; set; } = AlertSeverity.Medium;

    public JobRecurrenceKind RecurrenceKind { get; set; } = JobRecurrenceKind.Daily;

    public int IntervalMinutes { get; set; }

    public TimeOnly TimeOfDay { get; set; } = new(6, 0);

    public int? DayOfWeek { get; set; }

    /// <summary>No second alert for the same rule and group key while an open alert was seen within this many hours; the open alert counts the repeat instead.</summary>
    public int CooldownHours { get; set; } = 24;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? LastEvaluatedAt { get; set; }

    public RuleEvaluationOutcome? LastOutcome { get; set; }

    public string? LastError { get; set; }
}

/// <summary>Who receives a rule's alerts: a user, or every active member of a role, of the same organization.</summary>
public class MonitoringRuleRecipient : ITenantOwned
{
    public Guid RecipientId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid RuleId { get; set; }

    public Guid? UserId { get; set; }

    public Guid? RoleId { get; set; }
}

/// <summary>Append-only record of every evaluation run (scheduled or a test run).</summary>
public class RuleEvaluation : ITenantOwned
{
    public Guid EvaluationId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid RuleId { get; set; }

    public bool IsTest { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset FinishedAt { get; set; }

    public RuleEvaluationOutcome Outcome { get; set; }

    public int MatchCount { get; set; }

    public int AlertsRaised { get; set; }

    public long RowsExamined { get; set; }

    public string? ErrorCode { get; set; }
}

/// <summary>
/// What a rule produced (specification §35.2). One open alert per rule and group key: repeats within the cooldown
/// increase <see cref="Occurrences"/>. Content is aggregates and configured group keys only, never raw rows.
/// Status changes are human actions recorded as <see cref="AlertEvent"/>s.
/// </summary>
public class Alert : AuditableEntity, ITenantOwned
{
    public Guid AlertId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid RuleId { get; set; }

    public AlertSeverity Severity { get; set; }

    public AlertStatus Status { get; set; } = AlertStatus.Open;

    /// <summary>The value(s) the rule grouped by (an account number, a user id), or "*" for an ungrouped rule.</summary>
    public string GroupKey { get; set; } = "*";

    /// <summary>For access rules: the user whose behaviour the alert is about.</summary>
    public Guid? SubjectUserId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>JSON: measure, threshold, comparison and group values of the latest occurrence.</summary>
    public string SummaryJson { get; set; } = "{}";

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public int Occurrences { get; set; } = 1;

    public Guid? StatusChangedByUserId { get; set; }

    public DateTimeOffset? StatusChangedAt { get; set; }

    public string? Note { get; set; }
}

/// <summary>Append-only timeline of an alert.</summary>
public class AlertEvent : ITenantOwned
{
    public Guid EventId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid AlertId { get; set; }

    public AlertEventType EventType { get; set; }

    public Guid? ActorUserId { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
