using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Monitoring;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Entities.Support;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Monitoring;
using WabiInsight.Core.Notifications;
using WabiInsight.Core.Tenancy;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Authentication;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Monitoring;

public enum RuleRunMode
{
    /// <summary>Scheduled run: records the evaluation, raises alerts, pauses an invalid rule.</summary>
    Scheduled = 1,

    /// <summary>"Test now" on a saved rule: records a test evaluation, raises nothing.</summary>
    Test = 2,

    /// <summary>An unsaved definition: records nothing.</summary>
    Draft = 3,
}

public sealed record RuleRunOutput(RuleRunResult Result, int AlertsRaised);

/// <summary>An organization user who receives a rule's alerts (the owner always does).</summary>
public sealed record MonitoringRecipient(Guid UserId, string Email, string FullName, string Language);

/// <summary>
/// Runs one rule: the evaluation happens in a child scope initialized with the <b>owner's</b> principal (so every
/// permission check is the owner's), the outcome is persisted in the caller's scope (the scheduler, or an
/// Organization Administrator / the owner testing it). Alerts follow the cooldown: one open alert per rule and
/// group key, repeats counted on it.
/// </summary>
public sealed class RuleRunner
{
    public const int MaxAlertsPerRun = 100;

    private static readonly JsonSerializerOptions SummaryJson = new(JsonSerializerDefaults.Web);

    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IServiceScopeFactory _scopes;
    private readonly INotificationDispatcher _notifications;
    private readonly IUsageEventWriter _usage;
    private readonly IClock _clock;
    private Guid? _organizationId;

    public RuleRunner(ConfigurationDbContext context, ITenantContext tenantContext, IServiceScopeFactory scopes, INotificationDispatcher notifications, IUsageEventWriter usage, IClock clock)
    {
        _context = context;
        _tenantContext = tenantContext;
        _scopes = scopes;
        _notifications = notifications;
        _usage = usage;
        _clock = clock;
    }

    public async Task<RuleRunOutput> RunAsync(MonitoringRule rule, RuleRunMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var organizationId = rule.OrganizationId;
        if (!_tenantContext.IsPlatformAdministrator && _tenantContext.OrganizationId != organizationId)
        {
            throw new TenantContextException("Rules run inside their own organization.");
        }

        _organizationId = organizationId;
        var started = _clock.UtcNow;
        var organization = await _context.Organizations.AsNoTracking().SingleAsync(o => o.OrganizationId == organizationId, cancellationToken);
        var owner = await Org(_context.OrganizationUsers).AsNoTracking().SingleOrDefaultAsync(u => u.UserId == rule.OwnerUserId, cancellationToken);
        RuleRunResult result;
        if (organization.Status != OrganizationStatus.Active)
        {
            result = new RuleRunResult(RuleEvaluationOutcome.Skipped, [], 0, "organization_inactive", false);
        }
        else if (owner is null || owner.Status != OrganizationUserStatus.Active)
        {
            result = RuleRunResult.Fail("rule_owner_inactive", invalid: true);
        }
        else
        {
            var roles = await AccountResolver.RoleCodesAsync(_context, owner, cancellationToken);
            await using var scope = _scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantContextInitializer>().Initialize(TenantPrincipal.ForOrganizationUser(owner.OrganizationId, owner.UserId, owner.FullName, roles, owner.DepartmentId, owner.SectionId));
            result = await scope.ServiceProvider.GetRequiredService<RuleEvaluator>().EvaluateAsync(rule.Kind, RuleDefinition.Parse(rule.DefinitionJson), organization, cancellationToken);
        }

        if (mode == RuleRunMode.Draft)
        {
            return new RuleRunOutput(result, 0);
        }

        var raised = mode == RuleRunMode.Scheduled && result.Outcome == RuleEvaluationOutcome.Fired ? await RaiseAsync(rule, owner, result.Matches, cancellationToken) : 0;
        var finished = _clock.UtcNow;
        _context.RuleEvaluations.Add(new RuleEvaluation
        {
            EvaluationId = Ids.New(),
            OrganizationId = organizationId,
            RuleId = rule.RuleId,
            IsTest = mode == RuleRunMode.Test,
            StartedAt = started,
            FinishedAt = finished,
            Outcome = result.Outcome,
            MatchCount = result.Matches.Count,
            AlertsRaised = raised,
            RowsExamined = result.RowsExamined,
            ErrorCode = result.ErrorCode,
        });

        if (mode == RuleRunMode.Scheduled)
        {
            var tracked = await Org(_context.MonitoringRules).SingleOrDefaultAsync(r => r.RuleId == rule.RuleId, cancellationToken);
            if (tracked is not null)
            {
                tracked.LastEvaluatedAt = finished;
                tracked.LastOutcome = result.Outcome;
                tracked.LastError = result.ErrorCode;
                if (result.Invalid && tracked.IsActive)
                {
                    await PauseAsync(tracked, owner, result.ErrorCode!, cancellationToken);
                }
            }
        }

        _usage.Append(UsageEventTypes.RuleEvaluated, "MonitoringRule", rule.RuleId.ToString(), quantity: result.Matches.Count, succeeded: result.Outcome != RuleEvaluationOutcome.Failed,
            metadata: new { kind = rule.Kind.ToString(), outcome = result.Outcome.ToString(), test = mode == RuleRunMode.Test, raised, result.ErrorCode, result.RowsExamined, ms = (finished - started).TotalMilliseconds }, organizationId: organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return new RuleRunOutput(result, raised);
    }

    /// <summary>Owner plus explicit users plus active members of recipient roles; active users of the rule's organization only.</summary>
    public static async Task<IReadOnlyList<MonitoringRecipient>> ResolveRecipientsAsync(ConfigurationDbContext context, ITenantContext tenantContext, Guid organizationId, IReadOnlyCollection<MonitoringRule> rules, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rules);
        var ruleIds = rules.Select(r => r.RuleId).ToList();
        var recipientRows = await Scope(context.MonitoringRuleRecipients, tenantContext, organizationId).AsNoTracking().Where(r => ruleIds.Contains(r.RuleId)).ToListAsync(cancellationToken);
        var roleIds = recipientRows.Where(r => r.RoleId is not null).Select(r => r.RoleId!.Value).Distinct().ToList();
        var roleMembers = await Scope(context.UserRoles, tenantContext, organizationId).AsNoTracking().Where(ur => roleIds.Contains(ur.RoleId)).Select(ur => ur.UserId).ToListAsync(cancellationToken);
        var userIds = rules.Select(r => r.OwnerUserId).Concat(recipientRows.Where(r => r.UserId is not null).Select(r => r.UserId!.Value)).Concat(roleMembers).Distinct().ToList();
        return await Scope(context.OrganizationUsers, tenantContext, organizationId).AsNoTracking()
            .Where(u => userIds.Contains(u.UserId) && u.Status == OrganizationUserStatus.Active)
            .Select(u => new MonitoringRecipient(u.UserId, u.Email, u.FullName, u.PreferredLanguage))
            .ToListAsync(cancellationToken);
    }

    internal static IQueryable<T> Scope<T>(IQueryable<T> query, ITenantContext tenantContext, Guid organizationId)
        where T : class, ITenantOwned
        => (tenantContext.IsPlatformAdministrator ? query.AsPlatformScope(tenantContext) : query).Where(e => e.OrganizationId == organizationId);

    private IQueryable<T> Org<T>(IQueryable<T> query)
        where T : class, ITenantOwned => Scope(query, _tenantContext, _organizationId ?? throw new InvalidOperationException("RunAsync sets the organization."));

    private async Task<int> RaiseAsync(MonitoringRule rule, OrganizationUser? owner, IReadOnlyList<RuleMatch> matches, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var cooldown = TimeSpan.FromHours(Math.Max(0, rule.CooldownHours));
        var keys = matches.Select(m => m.GroupKey).Distinct().ToList();
        var existing = await Org(_context.Alerts).Where(a => a.RuleId == rule.RuleId && keys.Contains(a.GroupKey)).OrderByDescending(a => a.LastSeenAt).ToListAsync(cancellationToken);
        var subjects = matches.Where(m => m.SubjectUserId is not null).Select(m => m.SubjectUserId!.Value).Distinct().ToList();
        var subjectNames = await Org(_context.OrganizationUsers).AsNoTracking().Where(u => subjects.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.FullName, cancellationToken);
        var notify = new List<Alert>();
        foreach (var match in matches.Take(MaxAlertsPerRun))
        {
            var summary = JsonSerializer.Serialize(match.Summary, SummaryJson);
            var open = existing.FirstOrDefault(a => a.GroupKey == match.GroupKey && a.Status is AlertStatus.Open or AlertStatus.Acknowledged);
            if (open is not null)
            {
                // Still open: count the repeat. Outside the cooldown it is announced again.
                var renotify = now - open.LastSeenAt >= cooldown;
                open.Occurrences++;
                open.LastSeenAt = now;
                open.SummaryJson = summary;
                AddEvent(open, AlertEventType.Repeated, null, null, now);
                if (renotify)
                {
                    notify.Add(open);
                }

                continue;
            }

            var closed = existing.FirstOrDefault(a => a.GroupKey == match.GroupKey && a.StatusChangedAt is { } at && now - at < cooldown);
            if (closed is not null)
            {
                continue; // Resolved or dismissed within the cooldown: a person already looked at it.
            }

            var alert = new Alert
            {
                AlertId = Ids.New(),
                OrganizationId = rule.OrganizationId,
                RuleId = rule.RuleId,
                Severity = rule.Severity,
                GroupKey = match.GroupKey,
                SubjectUserId = match.SubjectUserId,
                Title = Truncate(match.SubjectUserId is { } s && subjectNames.TryGetValue(s, out var name) ? $"{rule.Name} — {name}" : rule.Name, 300),
                SummaryJson = summary,
                FirstSeenAt = now,
                LastSeenAt = now,
            };
            _context.Alerts.Add(alert);
            existing.Add(alert);
            AddEvent(alert, AlertEventType.Raised, null, null, now);
            notify.Add(alert);
        }

        if (notify.Count == 0)
        {
            return 0;
        }

        _usage.Append(UsageEventTypes.AlertRaised, "MonitoringRule", rule.RuleId.ToString(), quantity: notify.Count, metadata: new { severity = rule.Severity.ToString() }, organizationId: rule.OrganizationId);
        if (rule.Severity == AlertSeverity.High)
        {
            // D4: High also produces an immediate in-app notification; content is the rule name and count, never group values.
            var recipients = await ResolveRecipientsAsync(_context, _tenantContext, rule.OrganizationId, [rule], cancellationToken);
            _notifications.Notify(recipients.Select(r => new NotificationRecipient(PrincipalType.OrganizationUser, r.UserId, rule.OrganizationId, r.Email, r.FullName, r.Language)), NotificationTypes.MonitoringAlert, "Alert",
                notify[0].AlertId.ToString(), new { ruleId = rule.RuleId, ruleName = rule.Name, severity = rule.Severity.ToString(), alerts = notify.Count }, rule.OrganizationId);
        }

        _ = owner;
        return notify.Count;
    }

    private async Task PauseAsync(MonitoringRule rule, OrganizationUser? owner, string reason, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        rule.IsActive = false;
        var jobs = await _context.ScheduledJobs.Where(j => j.OrganizationId == rule.OrganizationId && j.JobType == ScheduledJobType.MonitoringRule && j.TargetId == rule.RuleId).ToListAsync(cancellationToken);
        foreach (var job in jobs)
        {
            job.IsEnabled = false;
            job.UpdatedAt = now;
        }

        // Specification §35.1: a rule that no longer matches the catalogue or its owner's access is paused and flagged, never run silently.
        var alert = new Alert
        {
            AlertId = Ids.New(),
            OrganizationId = rule.OrganizationId,
            RuleId = rule.RuleId,
            Severity = AlertSeverity.Medium,
            GroupKey = "rule_invalid",
            Title = Truncate(rule.Name, 300),
            SummaryJson = JsonSerializer.Serialize(new Dictionary<string, object?> { ["paused"] = true, ["reason"] = reason }, SummaryJson),
            FirstSeenAt = now,
            LastSeenAt = now,
        };
        _context.Alerts.Add(alert);
        AddEvent(alert, AlertEventType.Raised, null, reason, now);
        var userIds = new[] { rule.OwnerUserId, rule.CreatedByUserId }.Distinct().ToList();
        var users = await Org(_context.OrganizationUsers).AsNoTracking().Where(u => userIds.Contains(u.UserId) && u.Status == OrganizationUserStatus.Active).ToListAsync(cancellationToken);
        _notifications.Notify(users.Select(u => new NotificationRecipient(PrincipalType.OrganizationUser, u.UserId, rule.OrganizationId, u.Email, u.FullName, u.PreferredLanguage)), NotificationTypes.MonitoringRulePaused, "MonitoringRule",
            rule.RuleId.ToString(), new { ruleId = rule.RuleId, ruleName = rule.Name, reason }, rule.OrganizationId);
        _ = owner;
    }

    private void AddEvent(Alert alert, AlertEventType type, Guid? actor, string? note, DateTimeOffset at) => _context.AlertEvents.Add(new AlertEvent
    {
        EventId = Ids.New(),
        OrganizationId = alert.OrganizationId,
        AlertId = alert.AlertId,
        EventType = type,
        ActorUserId = actor,
        Note = note,
        CreatedAt = at,
    });

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>Scheduled evaluation of one rule (<see cref="ScheduledJob.TargetId"/> is the rule).</summary>
public sealed class MonitoringRuleJob : IScheduledJobHandler
{
    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly RuleRunner _runner;

    public MonitoringRuleJob(ConfigurationDbContext context, ITenantContext tenantContext, RuleRunner runner)
    {
        _context = context;
        _tenantContext = tenantContext;
        _runner = runner;
    }

    public ScheduledJobType JobType => ScheduledJobType.MonitoringRule;

    public async Task ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var organizationId = job.OrganizationId ?? throw new InvalidOperationException("Rules are per organization.");
        var rule = await RuleRunner.Scope(_context.MonitoringRules, _tenantContext, organizationId).AsNoTracking().SingleOrDefaultAsync(r => r.RuleId == job.TargetId, cancellationToken);
        if (rule is null || !rule.IsActive)
        {
            return; // The standing-job sync disables the job on its next pass.
        }

        await _runner.RunAsync(rule, RuleRunMode.Scheduled, cancellationToken);
    }
}
