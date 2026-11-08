using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Monitoring;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Entities.Support;
using WabiInsight.Core.Monitoring;
using WabiInsight.Core.Notifications;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Monitoring;

/// <summary>
/// Rule health (specification §35.1, P14.4). A rule that no longer matches the catalogue is paused and flagged
/// with a <c>rule_invalid</c> alert, never run silently. The scheduled runner pauses on evaluation; this checker
/// does it immediately after a metadata change (refresh, object or column edit, approval), so removing a column
/// pauses every active data rule that reads it.
/// </summary>
public sealed class RuleHealthChecker
{
    public const string InvalidGroupKey = "rule_invalid";

    private static readonly JsonSerializerOptions SummaryJson = new(JsonSerializerDefaults.Web);

    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly INotificationDispatcher _notifications;
    private readonly IClock _clock;

    public RuleHealthChecker(ConfigurationDbContext context, ITenantContext tenantContext, INotificationDispatcher notifications, IClock clock)
    {
        _context = context;
        _tenantContext = tenantContext;
        _notifications = notifications;
        _clock = clock;
    }

    /// <summary>
    /// Structural check of the organization's active data rules: the object must still be present and approved and
    /// every referenced column present and approved. An unavailable application is not a definition problem (it
    /// comes back), so it does not pause. Returns the number of rules paused; saves its own changes.
    /// </summary>
    public async Task<int> CheckOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var rules = await RuleRunner.Scope(_context.MonitoringRules, _tenantContext, organizationId)
            .Where(r => r.IsActive && r.Kind == MonitoringRuleKind.Data)
            .ToListAsync(cancellationToken);
        if (rules.Count == 0)
        {
            return 0;
        }

        var definitions = new List<(MonitoringRule Rule, DataRuleDefinition Data)>();
        foreach (var rule in rules)
        {
            DataRuleDefinition? data;
            try
            {
                data = RuleDefinition.Parse(rule.DefinitionJson).Data;
            }
            catch (JsonException)
            {
                data = null;
            }

            if (data is not null)
            {
                definitions.Add((rule, data));
            }
        }

        var objectIds = definitions.Select(d => d.Data.ObjectId).Distinct().ToList();
        var objects = await RuleRunner.Scope(_context.DatabaseObjects, _tenantContext, organizationId).AsNoTracking()
            .Where(o => objectIds.Contains(o.ObjectId))
            .ToDictionaryAsync(o => o.ObjectId, o => o.IsPresent && o.IsApproved, cancellationToken);
        var columns = await RuleRunner.Scope(_context.DatabaseColumns, _tenantContext, organizationId).AsNoTracking()
            .Where(c => objectIds.Contains(c.ObjectId))
            .Select(c => new { c.ColumnId, c.ObjectId, Usable = c.IsPresent && c.IsApproved })
            .ToListAsync(cancellationToken);
        var usable = columns.Where(c => c.Usable).Select(c => (c.ObjectId, c.ColumnId)).ToHashSet();

        var paused = 0;
        foreach (var (rule, data) in definitions)
        {
            var reason = !objects.TryGetValue(data.ObjectId, out var objectUsable) ? "rule_object_missing"
                : !objectUsable ? "rule_object_unavailable"
                : ReferencedColumns(data).Any(id => !usable.Contains((data.ObjectId, id))) ? "rule_column_unavailable"
                : null;
            if (reason is null)
            {
                continue;
            }

            await PauseAsync(rule, reason, cancellationToken);
            rule.LastError = reason;
            paused++;
        }

        if (paused > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return paused;
    }

    /// <summary>Pauses a tracked rule: disables its schedule, raises a <c>rule_invalid</c> alert and tells the owner and creator. The caller saves.</summary>
    public async Task PauseAsync(MonitoringRule rule, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var now = _clock.UtcNow;
        rule.IsActive = false;
        var jobs = await _context.ScheduledJobs
            .Where(j => j.OrganizationId == rule.OrganizationId && j.JobType == ScheduledJobType.MonitoringRule && j.TargetId == rule.RuleId)
            .ToListAsync(cancellationToken);
        foreach (var job in jobs)
        {
            job.IsEnabled = false;
            job.UpdatedAt = now;
        }

        var alert = new Alert
        {
            AlertId = Ids.New(),
            OrganizationId = rule.OrganizationId,
            RuleId = rule.RuleId,
            Severity = AlertSeverity.Medium,
            GroupKey = InvalidGroupKey,
            Title = rule.Name.Length <= 300 ? rule.Name : rule.Name[..300],
            SummaryJson = JsonSerializer.Serialize(new Dictionary<string, object?> { ["paused"] = true, ["reason"] = reason }, SummaryJson),
            FirstSeenAt = now,
            LastSeenAt = now,
        };
        _context.Alerts.Add(alert);
        _context.AlertEvents.Add(new AlertEvent
        {
            EventId = Ids.New(),
            OrganizationId = rule.OrganizationId,
            AlertId = alert.AlertId,
            EventType = AlertEventType.Raised,
            Note = reason,
            CreatedAt = now,
        });

        var userIds = new[] { rule.OwnerUserId, rule.CreatedByUserId }.Distinct().ToList();
        var users = await RuleRunner.Scope(_context.OrganizationUsers, _tenantContext, rule.OrganizationId).AsNoTracking()
            .Where(u => userIds.Contains(u.UserId) && u.Status == OrganizationUserStatus.Active)
            .ToListAsync(cancellationToken);
        _notifications.Notify(users.Select(u => new NotificationRecipient(PrincipalType.OrganizationUser, u.UserId, rule.OrganizationId, u.Email, u.FullName, u.PreferredLanguage)), NotificationTypes.MonitoringRulePaused, "MonitoringRule",
            rule.RuleId.ToString(), new { ruleId = rule.RuleId, ruleName = rule.Name, reason }, rule.OrganizationId);
    }

    private static IEnumerable<Guid> ReferencedColumns(DataRuleDefinition data)
        => data.GroupByColumnIds
            .Concat(data.Filters.Select(f => f.ColumnId))
            .Concat(data.TimeColumnId is { } t ? [t] : [])
            .Concat(data.Measure.ColumnId is { } m ? [m] : [])
            .Distinct();
}
