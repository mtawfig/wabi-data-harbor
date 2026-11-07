using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Monitoring;

namespace WabiInsight.Core.Monitoring;

public sealed record TemplateColumnOption(Guid ColumnId, string ColumnName, string? DisplayNameEn, string? DisplayNameAr, ColumnSemanticRole Role);

/// <summary>One permitted object of the owner that satisfies every slot of a data template, with the columns that can fill each slot.</summary>
public sealed record TemplateCandidate(Guid ObjectId, Guid ApplicationId, string ApplicationCode, string SchemaName, string ObjectName, string? DisplayNameEn, string? DisplayNameAr, IReadOnlyDictionary<string, IReadOnlyList<TemplateColumnOption>> Slots);

public sealed record RuleTemplateDto(
    string Code,
    MonitoringRuleKind Kind,
    AlertSeverity Severity,
    JobRecurrenceKind RecurrenceKind,
    TimeOnly TimeOfDay,
    int IntervalMinutes,
    bool Available,
    string? UnavailableReason,
    IReadOnlyList<TemplateSlot> Slots,
    IReadOnlyList<TemplateCandidate> Candidates,
    RuleDefinition Defaults);

public sealed record RuleRecipientRef(Guid? UserId, Guid? RoleId);

public sealed record RuleRecipientDto(Guid? UserId, Guid? RoleId, string Name);

public sealed record MonitoringRuleDto(
    Guid RuleId,
    string Name,
    string? Description,
    MonitoringRuleKind Kind,
    string? TemplateCode,
    RuleDefinition Definition,
    AlertSeverity Severity,
    JobRecurrenceKind RecurrenceKind,
    int IntervalMinutes,
    TimeOnly TimeOfDay,
    int? DayOfWeek,
    int CooldownHours,
    bool IsActive,
    Guid OwnerUserId,
    string OwnerName,
    Guid CreatedByUserId,
    IReadOnlyList<RuleRecipientDto> Recipients,
    string? ObjectName,
    DateTimeOffset? LastEvaluatedAt,
    RuleEvaluationOutcome? LastOutcome,
    string? LastError,
    DateTimeOffset? NextRunAt,
    int OpenAlerts,
    bool CanEdit,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record SaveMonitoringRuleCommand(
    string Name,
    string? Description,
    MonitoringRuleKind Kind,
    string? TemplateCode,
    RuleDefinition Definition,
    AlertSeverity Severity,
    JobRecurrenceKind RecurrenceKind,
    int IntervalMinutes,
    TimeOnly TimeOfDay,
    int? DayOfWeek,
    int CooldownHours,
    bool IsActive,
    Guid? OwnerUserId,
    IReadOnlyList<RuleRecipientRef>? Recipients);

/// <summary>One group that met the condition. <see cref="GroupValues"/> are null when the viewer may not read the rule's object or columns.</summary>
public sealed record RuleMatchDto(string? GroupKey, IReadOnlyList<string?>? GroupValues, decimal Measure, decimal? Previous, decimal? Threshold, Guid? SubjectUserId, string? SubjectName);

public sealed record RuleTestResultDto(RuleEvaluationOutcome Outcome, int MatchCount, long RowsExamined, string? ErrorCode, bool Redacted, IReadOnlyList<RuleMatchDto> Matches);

public sealed record MonitoringSettingsDto(TimeOnly DigestTime, TimeOnly WorkingHoursStart, TimeOnly WorkingHoursEnd, string WorkingDays, int RetentionMonths, int MaxRetentionMonths, string TimeZone);

public sealed record UpdateMonitoringSettingsCommand(TimeOnly DigestTime, TimeOnly WorkingHoursStart, TimeOnly WorkingHoursEnd, string WorkingDays, int RetentionMonths);

public sealed record OwnerOptionDto(Guid UserId, string FullName, string Email, IReadOnlyList<string> Roles, int PermittedObjects);

/// <summary>Monitoring Rules (specification §35.1). Organization Administrators manage every rule; Management users their own data rules; others nothing.</summary>
public interface IMonitoringRuleService
{
    Task<IReadOnlyList<RuleTemplateDto>> ListTemplatesAsync(Guid? ownerUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OwnerOptionDto>> ListOwnerOptionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MonitoringRuleDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<MonitoringRuleDto> GetAsync(Guid ruleId, CancellationToken cancellationToken = default);

    Task<MonitoringRuleDto> CreateAsync(SaveMonitoringRuleCommand command, CancellationToken cancellationToken = default);

    Task<MonitoringRuleDto> UpdateAsync(Guid ruleId, SaveMonitoringRuleCommand command, CancellationToken cancellationToken = default);

    Task<MonitoringRuleDto> SetActiveAsync(Guid ruleId, bool isActive, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid ruleId, CancellationToken cancellationToken = default);

    /// <summary>Evaluates a saved rule now as its owner; records a test evaluation; raises nothing.</summary>
    Task<RuleTestResultDto> TestAsync(Guid ruleId, CancellationToken cancellationToken = default);

    /// <summary>Evaluates an unsaved definition as its intended owner; records nothing.</summary>
    Task<RuleTestResultDto> TestDraftAsync(SaveMonitoringRuleCommand command, CancellationToken cancellationToken = default);

    Task<MonitoringSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task<MonitoringSettingsDto> UpdateSettingsAsync(UpdateMonitoringSettingsCommand command, CancellationToken cancellationToken = default);
}

public sealed record AlertQuery(AlertStatus? Status = null, AlertSeverity? Severity = null, Guid? RuleId = null, int Page = 1, int PageSize = 25);

public sealed record AlertDto(
    Guid AlertId,
    Guid RuleId,
    string RuleName,
    MonitoringRuleKind? RuleKind,
    AlertSeverity Severity,
    AlertStatus Status,
    string? GroupKey,
    Guid? SubjectUserId,
    string? SubjectName,
    OrganizationUserSummary? Subject,
    string Title,
    IReadOnlyDictionary<string, object?>? Summary,
    bool Redacted,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    int Occurrences,
    DateTimeOffset? StatusChangedAt,
    string? StatusChangedByName,
    string? Note,
    bool CanAct,
    bool CanSuspendSubject);

public sealed record OrganizationUserSummary(Guid UserId, string FullName, string Email, string Status);

public sealed record AlertEventDto(AlertEventType EventType, string? ActorName, string? Note, DateTimeOffset CreatedAt);

public sealed record AlertDetailDto(AlertDto Alert, IReadOnlyList<AlertEventDto> Events);

public sealed record AlertPageDto(IReadOnlyList<AlertDto> Items, int Total, int Page, int PageSize, int OpenCount, int OpenHighCount);

/// <summary>Alerts (specification §35.2): visible to Organization Administrators, the rule owner and the rule's recipients; status changes and suspension are human actions.</summary>
public interface IAlertService
{
    Task<AlertPageDto> ListAsync(AlertQuery query, CancellationToken cancellationToken = default);

    Task<AlertDetailDto> GetAsync(Guid alertId, CancellationToken cancellationToken = default);

    Task<AlertDetailDto> ChangeStatusAsync(Guid alertId, AlertStatus status, string? note, CancellationToken cancellationToken = default);

    /// <summary>Organization Administrators only, access alerts about a user only: sets the user Inactive through the user service (D2: never automatic).</summary>
    Task<AlertDetailDto> SuspendSubjectAsync(Guid alertId, string? note, CancellationToken cancellationToken = default);

    /// <summary>The digest the caller would receive now (last 24 hours).</summary>
    Task<DigestPreviewDto> PreviewDigestAsync(CancellationToken cancellationToken = default);
}

public sealed record DigestLine(AlertSeverity Severity, string RuleName, string Title, int Occurrences, bool Redacted, string? GroupKey, decimal? Measure);

public sealed record DigestPreviewDto(string Subject, IReadOnlyList<DigestLine> Lines);

public sealed record OrganizationMonitoringCounts(Guid OrganizationId, string OrganizationCode, string OrganizationName, int ActiveRules, int OpenAlerts, int OpenHighAlerts, int EvaluationsLast24Hours, int FailedEvaluationsLast24Hours);

/// <summary>Platform Administrators see counts per organization only, never alert content (specification §35.5).</summary>
public interface IPlatformMonitoringService
{
    Task<IReadOnlyList<OrganizationMonitoringCounts>> SummaryAsync(CancellationToken cancellationToken = default);
}
