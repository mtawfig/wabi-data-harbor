using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Common;
using WabiInsight.Core.Email;
using WabiInsight.Core.Entities.Monitoring;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Monitoring;
using WabiInsight.Core.Permissions;
using WabiInsight.Core.Tenancy;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Authentication;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Monitoring;

/// <summary>
/// Builds digest lines per recipient (owner decision D4). A recipient sees an alert of a data rule in full only when
/// they own the rule, or their own permissions cover the rule's object and the columns shown (group-by and measure)
/// with row scope ALL; otherwise title and count only. Shared by the daily job and the "preview my digest" endpoint.
/// </summary>
public sealed class AlertDigestBuilder
{
    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IPermissionResolver _permissions;

    public AlertDigestBuilder(ConfigurationDbContext context, ITenantContext tenantContext, IPermissionResolver permissions)
    {
        _context = context;
        _tenantContext = tenantContext;
        _permissions = permissions;
    }

    public async Task<IReadOnlyList<(MonitoringRecipient Recipient, IReadOnlyList<DigestLine> Lines)>> BuildAsync(Guid organizationId, DateTimeOffset since, Guid? onlyUserId, CancellationToken cancellationToken)
    {
        var alerts = await RuleRunner.Scope(_context.Alerts, _tenantContext, organizationId).AsNoTracking()
            .Where(a => a.LastSeenAt > since && (a.Status == AlertStatus.Open || a.Status == AlertStatus.Acknowledged))
            .OrderByDescending(a => a.LastSeenAt)
            .Take(1000)
            .ToListAsync(cancellationToken);
        if (alerts.Count == 0)
        {
            return [];
        }

        var ruleIds = alerts.Select(a => a.RuleId).Distinct().ToList();
        var rules = await RuleRunner.Scope(_context.MonitoringRules, _tenantContext, organizationId).AsNoTracking().Where(r => ruleIds.Contains(r.RuleId)).ToDictionaryAsync(r => r.RuleId, cancellationToken);
        var byUser = new Dictionary<Guid, (MonitoringRecipient Recipient, List<DigestLine> Lines)>();
        var permissions = new Dictionary<Guid, EffectivePermissions>();
        foreach (var rule in rules.Values)
        {
            var recipients = await RuleRunner.ResolveRecipientsAsync(_context, _tenantContext, organizationId, [rule], cancellationToken);
            var data = rule.Kind == MonitoringRuleKind.Data ? RuleDefinition.Parse(rule.DefinitionJson).Data : null;
            foreach (var recipient in recipients.Where(r => onlyUserId is null || r.UserId == onlyUserId))
            {
                if (!byUser.TryGetValue(recipient.UserId, out var entry))
                {
                    entry = (recipient, []);
                    byUser[recipient.UserId] = entry;
                }

                EffectivePermissions? viewer = null;
                if (data is not null && !permissions.TryGetValue(recipient.UserId, out viewer))
                {
                    viewer = await _permissions.ResolveForUserAsync(organizationId, recipient.UserId, cancellationToken);
                    permissions[recipient.UserId] = viewer;
                }

                foreach (var alert in alerts.Where(a => a.RuleId == rule.RuleId))
                {
                    // Judged per alert against the owner the alert was evaluated as (P13.26), not the rule's current owner.
                    var canSee = data is null || RuleCatalog.CanSeeDetails(viewer!, data, alert.EvaluatedAsUserId ?? Guid.Empty);
                    entry.Lines.Add(Line(rule, alert, canSee));
                }
            }
        }

        return byUser.Values.Where(v => v.Lines.Count > 0).Select(v => (v.Recipient, (IReadOnlyList<DigestLine>)v.Lines)).ToList();
    }

    internal static DigestLine Line(MonitoringRule rule, Alert alert, bool canSeeDetails)
    {
        if (alert.GroupKey == "rule_invalid")
        {
            var reason = Summary(alert).GetValueOrDefault("reason")?.ToString();
            return new DigestLine(alert.Severity, rule.Name, alert.Title, alert.Occurrences, false, $"paused ({reason})", null);
        }

        var measure = Summary(alert).GetValueOrDefault("measure") is { } m && decimal.TryParse(m.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : (decimal?)null;
        if (rule.Kind != MonitoringRuleKind.Data)
        {
            return new DigestLine(alert.Severity, rule.Name, alert.Title, alert.Occurrences, false, null, measure);
        }

        return canSeeDetails
            ? new DigestLine(alert.Severity, rule.Name, alert.Title, alert.Occurrences, false, alert.GroupKey, measure)
            : new DigestLine(alert.Severity, rule.Name, alert.Title, alert.Occurrences, true, null, null);
    }

    internal static Dictionary<string, object?> Summary(Alert alert)
    {
        try
        {
            using var document = JsonDocument.Parse(alert.SummaryJson);
            return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)(p.Value.ValueKind switch
            {
                JsonValueKind.Number => p.Value.GetDecimal(),
                JsonValueKind.String => p.Value.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Array => p.Value.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Null ? null : v.ToString()).ToList(),
                JsonValueKind.Null => null,
                _ => p.Value.ToString(),
            }), StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>Once a day per organization at its digest time: one email per recipient with at least one alert since the previous digest; nobody gets an empty digest.</summary>
public sealed class AlertDigestJob : IScheduledJobHandler
{
    private readonly ConfigurationDbContext _context;
    private readonly AlertDigestBuilder _builder;
    private readonly IEmailOutbox _outbox;
    private readonly IUsageEventWriter _usage;
    private readonly IClock _clock;
    private readonly PlatformUrlOptions _urls;
    private readonly EmailOptions _email;

    public AlertDigestJob(ConfigurationDbContext context, AlertDigestBuilder builder, IEmailOutbox outbox, IUsageEventWriter usage, IClock clock, IOptions<PlatformUrlOptions> urls, IOptions<EmailOptions> email)
    {
        _context = context;
        _builder = builder;
        _outbox = outbox;
        _usage = usage;
        _clock = clock;
        _urls = urls.Value;
        _email = email.Value;
    }

    public ScheduledJobType JobType => ScheduledJobType.AlertDigest;

    public const int MaxLookBackDays = 7;

    /// <summary>From the last successful digest, at most a week back; the first digest covers the last 24 hours.</summary>
    public static DateTimeOffset DigestWindowStart(DateTimeOffset? lastSuccessAt, DateTimeOffset now)
    {
        var oldest = now.AddDays(-MaxLookBackDays);
        return lastSuccessAt is { } last ? (last > oldest ? last : oldest) : now.AddHours(-24);
    }

    public async Task ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var organizationId = job.OrganizationId ?? throw new InvalidOperationException("Digests are per organization.");
        var now = _clock.UtcNow;
        // Everything since the last digest that went out (P13.27): a failed run, a retry or a server that was down for a
        // few days no longer drops the alerts in between. A week is the furthest a digest looks back.
        var since = DigestWindowStart(job.LastSuccessAt, now);
        var organization = await _context.Organizations.AsNoTracking().SingleAsync(o => o.OrganizationId == organizationId, cancellationToken);
        var digests = await _builder.BuildAsync(organizationId, since, null, cancellationToken);
        var link = $"{_urls.BaseUrl.TrimEnd('/')}/app/alerts";
        foreach (var (recipient, lines) in digests)
        {
            var ar = string.Equals(recipient.Language, "ar", StringComparison.OrdinalIgnoreCase);
            var message = EmailTemplates.AlertDigest(recipient.Email, recipient.FullName, recipient.Language, ar ? organization.OrganizationNameAr : organization.OrganizationName, lines, link, _email.ReplyTo);
            await _outbox.EnqueueAsync(message, EmailTemplateCodes.MonitoringDigest, organizationId, cancellationToken);
        }

        await _usage.AppendAndSaveAsync(UsageEventTypes.DigestSent, "Organization", organizationId.ToString(), quantity: digests.Count,
            metadata: new { alerts = digests.Sum(d => d.Lines.Count), since }, organizationId: organizationId, cancellationToken: cancellationToken);
    }
}
