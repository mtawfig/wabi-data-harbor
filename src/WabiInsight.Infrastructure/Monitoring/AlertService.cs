using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Email;
using WabiInsight.Core.Entities.Monitoring;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Monitoring;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Permissions;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Monitoring;

/// <summary>
/// The alert centre (specification §35.2). An alert is visible to Organization Administrators, its rule's owner and
/// author, and the rule's recipients (users or role members). Data-rule details (group values, measures) are shown
/// only when the viewer's own permissions cover the rule's object and columns. Every status change is a human
/// action recorded as an <see cref="AlertEvent"/> and an audit entry; suspension (D2) goes through the user service.
/// </summary>
public sealed class AlertService : IAlertService
{
    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IPermissionResolver _permissions;
    private readonly IOrganizationUserService _users;
    private readonly AlertDigestBuilder _digest;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;

    public AlertService(ConfigurationDbContext context, ITenantContext tenantContext, IPermissionResolver permissions, IOrganizationUserService users, AlertDigestBuilder digest, IAuditWriter audit, IClock clock)
    {
        _context = context;
        _tenantContext = tenantContext;
        _permissions = permissions;
        _users = users;
        _digest = digest;
        _audit = audit;
        _clock = clock;
    }

    private Guid Self => _tenantContext.Principal.UserId ?? throw new TenantContextException("An organization user is required.");

    private bool IsAdmin => _tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin);

    public async Task<AlertPageDto> ListAsync(AlertQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        RequireUser();
        var visible = await VisibleRuleIdsAsync(cancellationToken);
        var alerts = _context.Alerts.AsNoTracking();
        if (visible is not null)
        {
            alerts = alerts.Where(a => visible.Contains(a.RuleId));
        }

        var openCount = await alerts.CountAsync(a => a.Status == AlertStatus.Open || a.Status == AlertStatus.Acknowledged, cancellationToken);
        var openHigh = await alerts.CountAsync(a => (a.Status == AlertStatus.Open || a.Status == AlertStatus.Acknowledged) && a.Severity == AlertSeverity.High, cancellationToken);
        if (query.Status is { } status)
        {
            alerts = alerts.Where(a => a.Status == status);
        }

        if (query.Severity is { } severity)
        {
            alerts = alerts.Where(a => a.Severity == severity);
        }

        if (query.RuleId is { } ruleId)
        {
            alerts = alerts.Where(a => a.RuleId == ruleId);
        }

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 100);
        var total = await alerts.CountAsync(cancellationToken);
        var items = await alerts.OrderByDescending(a => a.LastSeenAt).Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
        return new AlertPageDto(await MapAsync(items, cancellationToken), total, page, size, openCount, openHigh);
    }

    public async Task<AlertDetailDto> GetAsync(Guid alertId, CancellationToken cancellationToken = default)
    {
        RequireUser();
        var alert = await FindVisibleAsync(alertId, tracked: false, cancellationToken);
        return await DetailAsync(alert, cancellationToken);
    }

    public async Task<AlertDetailDto> ChangeStatusAsync(Guid alertId, AlertStatus status, string? note, CancellationToken cancellationToken = default)
    {
        RequireUser();
        var alert = await FindVisibleAsync(alertId, tracked: true, cancellationToken);
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is { Length: > 2000 })
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "alert_note_too_long");
        }

        var allowed = (alert.Status, status) switch
        {
            (AlertStatus.Open, AlertStatus.Acknowledged) => true,
            (AlertStatus.Open or AlertStatus.Acknowledged, AlertStatus.Resolved or AlertStatus.Dismissed) => true,
            (AlertStatus.Acknowledged or AlertStatus.Resolved or AlertStatus.Dismissed, AlertStatus.Open) => true,
            _ => false,
        };
        if (!allowed)
        {
            throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, "alert_status_transition_invalid");
        }

        if (status is AlertStatus.Resolved or AlertStatus.Dismissed && trimmed is null)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "alert_note_required");
        }

        var previous = alert.Status;
        var now = _clock.UtcNow;
        alert.Status = status;
        alert.StatusChangedAt = now;
        alert.StatusChangedByUserId = Self;
        alert.Note = trimmed ?? alert.Note;
        AddEvent(alert, status switch
        {
            AlertStatus.Acknowledged => AlertEventType.Acknowledged,
            AlertStatus.Resolved => AlertEventType.Resolved,
            AlertStatus.Dismissed => AlertEventType.Dismissed,
            _ => AlertEventType.Reopened,
        }, trimmed, now);
        _audit.Append(AuditEvents.AlertStatusChanged, "Alert", alertId.ToString(), new { previous = previous.ToString(), status = status.ToString(), rule = alert.RuleId });
        await _context.SaveChangesAsync(cancellationToken);
        return await DetailAsync(alert, cancellationToken);
    }

    public async Task<AlertDetailDto> SuspendSubjectAsync(Guid alertId, string? note, CancellationToken cancellationToken = default)
    {
        RequireUser();
        if (!IsAdmin)
        {
            throw new TenantContextException("Only Organization Administrators can suspend users.");
        }

        var alert = await FindVisibleAsync(alertId, tracked: true, cancellationToken);
        if (alert.SubjectUserId is not { } subjectId)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "alert_has_no_subject");
        }

        var subject = await _context.OrganizationUsers.AsNoTracking().SingleOrDefaultAsync(u => u.UserId == subjectId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "user_not_found");
        if (subject.Status != OrganizationUserStatus.Active)
        {
            throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, "user_not_active");
        }

        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        var now = _clock.UtcNow;
        // D2: a person decides. The event and the audit entry are saved with the status change by the user service.
        AddEvent(alert, AlertEventType.UserSuspended, trimmed is { Length: > 2000 } ? trimmed[..2000] : trimmed, now);
        if (alert.Status == AlertStatus.Open)
        {
            alert.Status = AlertStatus.Acknowledged;
            alert.StatusChangedAt = now;
            alert.StatusChangedByUserId = Self;
        }

        _audit.Append(AuditEvents.AlertUserSuspended, "Alert", alertId.ToString(), new { subject = subjectId, rule = alert.RuleId });
        await _users.SetStatusAsync(subjectId, OrganizationUserStatus.Inactive, cancellationToken);
        return await DetailAsync(alert, cancellationToken);
    }

    public async Task<DigestPreviewDto> PreviewDigestAsync(CancellationToken cancellationToken = default)
    {
        var organizationId = RequireUser();
        var digests = await _digest.BuildAsync(organizationId, _clock.UtcNow.AddHours(-24), Self, cancellationToken);
        var lines = digests.SelectMany(d => d.Lines).OrderByDescending(l => l.Severity).ToList();
        var language = digests.Count > 0 ? digests[0].Recipient.Language : "en";
        return new DigestPreviewDto(EmailTemplates.DigestSubject(language == "ar", lines.Count), lines);
    }

    private async Task<AlertDetailDto> DetailAsync(Alert alert, CancellationToken cancellationToken)
    {
        var dto = (await MapAsync([alert], cancellationToken))[0];
        var events = await _context.AlertEvents.AsNoTracking().Where(e => e.AlertId == alert.AlertId).OrderBy(e => e.CreatedAt).ToListAsync(cancellationToken);
        var actorIds = events.Where(e => e.ActorUserId != null).Select(e => e.ActorUserId!.Value).Distinct().ToList();
        var actors = await _context.OrganizationUsers.AsNoTracking().Where(u => actorIds.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.FullName, cancellationToken);
        return new AlertDetailDto(dto, events.Select(e => new AlertEventDto(e.EventType, e.ActorUserId is { } a ? actors.GetValueOrDefault(a) : null, e.Note, e.CreatedAt)).ToList());
    }

    private async Task<IReadOnlyList<AlertDto>> MapAsync(IReadOnlyList<Alert> alerts, CancellationToken cancellationToken)
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        var ruleIds = alerts.Select(a => a.RuleId).Distinct().ToList();
        var rules = await _context.MonitoringRules.AsNoTracking().Where(r => ruleIds.Contains(r.RuleId)).ToDictionaryAsync(r => r.RuleId, cancellationToken);
        var userIds = alerts.Where(a => a.SubjectUserId != null).Select(a => a.SubjectUserId!.Value)
            .Concat(alerts.Where(a => a.StatusChangedByUserId != null).Select(a => a.StatusChangedByUserId!.Value)).Distinct().ToList();
        var users = await _context.OrganizationUsers.AsNoTracking().Where(u => userIds.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, cancellationToken);
        EffectivePermissions? viewer = null;
        var details = new Dictionary<Guid, bool>();
        foreach (var rule in rules.Values.Where(r => r.Kind == MonitoringRuleKind.Data))
        {
            viewer ??= await _permissions.ResolveForUserAsync(organizationId, Self, cancellationToken);
            details[rule.RuleId] = RuleDefinition.Parse(rule.DefinitionJson).Data is { } data && RuleCatalog.CanSeeDetails(viewer, data);
        }

        return alerts.Select(a =>
        {
            var rule = rules.GetValueOrDefault(a.RuleId);
            var invalid = a.GroupKey == "rule_invalid";
            // Data rules: details only with the viewer's own permissions; a deleted rule's data alert stays redacted.
            var redacted = !invalid && (rule is null ? a.SubjectUserId is null : rule.Kind == MonitoringRuleKind.Data && !details.GetValueOrDefault(a.RuleId));
            var subject = a.SubjectUserId is { } s && users.TryGetValue(s, out var u) ? new OrganizationUserSummary(u.UserId, u.FullName, u.Email, u.Status.ToString()) : null;
            return new AlertDto(
                a.AlertId, a.RuleId, rule?.Name ?? string.Empty, rule?.Kind, a.Severity, a.Status,
                redacted || a.SubjectUserId is not null ? null : a.GroupKey,
                a.SubjectUserId, subject?.FullName, subject, a.Title,
                redacted ? null : AlertDigestBuilder.Summary(a), redacted,
                a.FirstSeenAt, a.LastSeenAt, a.Occurrences, a.StatusChangedAt,
                a.StatusChangedByUserId is { } by && users.TryGetValue(by, out var actor) ? actor.FullName : null,
                a.Note, true,
                IsAdmin && subject is { Status: nameof(OrganizationUserStatus.Active) } && subject.UserId != Self);
        }).ToList();
    }

    /// <summary>Null for administrators (every alert of the organization); otherwise the rules the caller owns, wrote or receives.</summary>
    private async Task<List<Guid>?> VisibleRuleIdsAsync(CancellationToken cancellationToken)
    {
        if (IsAdmin)
        {
            return null;
        }

        var self = Self;
        var myRoles = await _context.UserRoles.AsNoTracking().Where(ur => ur.UserId == self).Select(ur => ur.RoleId).ToListAsync(cancellationToken);
        var owned = await _context.MonitoringRules.AsNoTracking().Where(r => r.OwnerUserId == self || r.CreatedByUserId == self).Select(r => r.RuleId).ToListAsync(cancellationToken);
        var received = await _context.MonitoringRuleRecipients.AsNoTracking().Where(r => r.UserId == self || (r.RoleId != null && myRoles.Contains(r.RoleId.Value))).Select(r => r.RuleId).ToListAsync(cancellationToken);
        return owned.Concat(received).Distinct().ToList();
    }

    private async Task<Alert> FindVisibleAsync(Guid alertId, bool tracked, CancellationToken cancellationToken)
    {
        var query = tracked ? _context.Alerts : _context.Alerts.AsNoTracking();
        var alert = await query.SingleOrDefaultAsync(a => a.AlertId == alertId, cancellationToken);
        var visible = await VisibleRuleIdsAsync(cancellationToken);
        if (alert is null || (visible is not null && !visible.Contains(alert.RuleId)))
        {
            throw new DomainException(OrganizationOperationOutcome.NotFound, "alert_not_found");
        }

        return alert;
    }

    private void AddEvent(Alert alert, AlertEventType type, string? note, DateTimeOffset at) => _context.AlertEvents.Add(new AlertEvent
    {
        EventId = Ids.New(),
        OrganizationId = alert.OrganizationId,
        AlertId = alert.AlertId,
        EventType = type,
        ActorUserId = Self,
        Note = note,
        CreatedAt = at,
    });

    private Guid RequireUser()
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        if (_tenantContext.Principal.PrincipalType != PrincipalType.OrganizationUser)
        {
            throw new TenantContextException("Alerts belong to organization users.");
        }

        return organizationId;
    }
}

/// <summary>Counts per organization for Platform Administrators; never titles, group keys or summaries (specification §35.5).</summary>
public sealed class PlatformMonitoringService : IPlatformMonitoringService
{
    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public PlatformMonitoringService(ConfigurationDbContext context, ITenantContext tenantContext, IClock clock)
    {
        _context = context;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<IReadOnlyList<OrganizationMonitoringCounts>> SummaryAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsPlatformAdministrator)
        {
            throw new TenantContextException("Platform administrators only.");
        }

        var since = _clock.UtcNow.AddHours(-24);
        var organizations = await _context.Organizations.AsNoTracking().OrderBy(o => o.OrganizationCode).Select(o => new { o.OrganizationId, o.OrganizationCode, o.OrganizationName }).ToListAsync(cancellationToken);
        var rules = await _context.MonitoringRules.AsPlatformScope(_tenantContext).AsNoTracking().Where(r => r.IsActive).GroupBy(r => r.OrganizationId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var open = await _context.Alerts.AsPlatformScope(_tenantContext).AsNoTracking().Where(a => a.Status == AlertStatus.Open || a.Status == AlertStatus.Acknowledged)
            .GroupBy(a => a.OrganizationId).Select(g => new { g.Key, Count = g.Count(), High = g.Count(a => a.Severity == AlertSeverity.High) }).ToDictionaryAsync(g => g.Key, cancellationToken);
        var evaluations = await _context.RuleEvaluations.AsPlatformScope(_tenantContext).AsNoTracking().Where(e => e.StartedAt >= since && !e.IsTest)
            .GroupBy(e => e.OrganizationId).Select(g => new { g.Key, Count = g.Count(), Failed = g.Count(e => e.Outcome == RuleEvaluationOutcome.Failed) }).ToDictionaryAsync(g => g.Key, cancellationToken);
        return organizations.Select(o => new OrganizationMonitoringCounts(
            o.OrganizationId, o.OrganizationCode, o.OrganizationName, rules.GetValueOrDefault(o.OrganizationId),
            open.TryGetValue(o.OrganizationId, out var a) ? a.Count : 0, a?.High ?? 0,
            evaluations.TryGetValue(o.OrganizationId, out var e) ? e.Count : 0, e?.Failed ?? 0)).ToList();
    }
}
