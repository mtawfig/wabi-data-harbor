using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Monitoring;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Monitoring;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Permissions;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Monitoring;

/// <summary>
/// Monitoring Rules (specification §35.1, owner decisions D5/D6). Organization Administrators create every kind of
/// rule for any active owner; Management users create data rules they own and receive themselves; nobody else
/// manages rules. Every save validates the definition against the catalogue and the <b>owner's</b> permissions.
/// </summary>
public sealed class MonitoringRuleService : IMonitoringRuleService
{
    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IPermissionResolver _permissions;
    private readonly RuleRunner _runner;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;
    private readonly MonitoringOptions _options;

    public MonitoringRuleService(ConfigurationDbContext context, ITenantContext tenantContext, IPermissionResolver permissions, RuleRunner runner, IAuditWriter audit, IClock clock, IOptions<MonitoringOptions> options)
    {
        _context = context;
        _tenantContext = tenantContext;
        _permissions = permissions;
        _runner = runner;
        _audit = audit;
        _clock = clock;
        _options = options.Value;
    }

    private Guid Self => _tenantContext.Principal.UserId ?? throw new TenantContextException("An organization user is required.");

    private bool IsAdmin => _tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin);

    private bool IsManagement => _tenantContext.Principal.Roles.Contains(SystemRoles.Management);

    public async Task<IReadOnlyList<RuleTemplateDto>> ListTemplatesAsync(Guid? ownerUserId, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireAuthor();
        var owner = ResolveOwner(ownerUserId);
        await RequireActiveUserAsync(owner, "rule_owner_invalid", cancellationToken);
        var permissions = await _permissions.ResolveForUserAsync(organizationId, owner, cancellationToken);
        var objects = permissions.Objects.Where(o => o.YieldsRows).ToList();
        var objectIds = objects.Select(o => o.ObjectId).ToList();
        var roleColumns = await _context.DatabaseColumns.AsNoTracking()
            .Where(c => objectIds.Contains(c.ObjectId) && c.SemanticRole != null && c.IsApproved && c.IsPresent)
            .Select(c => new { c.ObjectId, c.ColumnId, c.ColumnName, c.DisplayNameEn, c.DisplayNameAr, Role = c.SemanticRole!.Value, c.OrdinalPosition })
            .ToListAsync(cancellationToken);
        var restricted = await _context.DatabaseColumns.AsNoTracking().AnyAsync(c => c.Classification == DataClassification.Restricted && c.IsApproved && c.IsPresent, cancellationToken);

        var result = new List<RuleTemplateDto>();
        foreach (var template in RuleTemplates.All)
        {
            if (template.Kind == MonitoringRuleKind.Access)
            {
                var available = IsAdmin && (!template.RequiresRestrictedColumns || restricted);
                var reason = !IsAdmin ? "template_admin_only" : available ? null : "template_needs_restricted_columns";
                result.Add(new RuleTemplateDto(template.Code, template.Kind, template.Severity, template.RecurrenceKind, template.TimeOfDay, template.IntervalMinutes, available, reason, template.Slots, [], template.Defaults));
                continue;
            }

            var candidates = new List<TemplateCandidate>();
            foreach (var access in objects)
            {
                var permitted = access.Columns.Select(c => c.ColumnId).ToHashSet();
                var slots = new Dictionary<string, IReadOnlyList<TemplateColumnOption>>(StringComparer.Ordinal);
                foreach (var slot in template.Slots)
                {
                    var options = roleColumns.Where(c => c.ObjectId == access.ObjectId && permitted.Contains(c.ColumnId) && slot.Roles.Contains(c.Role))
                        .OrderBy(c => slot.Roles.ToList().IndexOf(c.Role)).ThenBy(c => c.OrdinalPosition)
                        .Select(c => new TemplateColumnOption(c.ColumnId, c.ColumnName, c.DisplayNameEn, c.DisplayNameAr, c.Role)).ToList();
                    if (options.Count == 0)
                    {
                        break;
                    }

                    slots[slot.Slot] = options;
                }

                if (slots.Count == template.Slots.Count)
                {
                    candidates.Add(new TemplateCandidate(access.ObjectId, access.ApplicationId, access.ApplicationCode, access.SchemaName, access.ObjectName, access.DisplayNameEn, access.DisplayNameAr, slots));
                }
            }

            result.Add(new RuleTemplateDto(template.Code, template.Kind, template.Severity, template.RecurrenceKind, template.TimeOfDay, template.IntervalMinutes, candidates.Count > 0, candidates.Count > 0 ? null : "template_needs_roles", template.Slots, candidates, template.Defaults));
        }

        return result;
    }

    public async Task<IReadOnlyList<OwnerOptionDto>> ListOwnerOptionsAsync(CancellationToken cancellationToken = default)
    {
        var organizationId = RequireAuthor();
        var users = await _context.OrganizationUsers.AsNoTracking().Where(u => u.Status == OrganizationUserStatus.Active).OrderBy(u => u.FullName).Take(500).ToListAsync(cancellationToken);
        if (!IsAdmin)
        {
            users = users.Where(u => u.UserId == Self).ToList();
        }

        var roles = await _context.UserRoles.AsNoTracking().Join(_context.Roles.Where(r => r.IsActive), ur => ur.RoleId, r => r.RoleId, (ur, r) => new { ur.UserId, r.RoleCode }).ToListAsync(cancellationToken);
        var result = new List<OwnerOptionDto>(users.Count);
        foreach (var user in users)
        {
            var permissions = await _permissions.ResolveForUserAsync(organizationId, user.UserId, cancellationToken);
            result.Add(new OwnerOptionDto(user.UserId, user.FullName, user.Email, roles.Where(r => r.UserId == user.UserId).Select(r => r.RoleCode).Order(StringComparer.Ordinal).ToList(), permissions.Objects.Count(o => o.YieldsRows)));
        }

        return result;
    }

    public async Task<IReadOnlyList<MonitoringRuleDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        RequireAuthor();
        var self = Self;
        var query = _context.MonitoringRules.AsNoTracking();
        if (!IsAdmin)
        {
            query = query.Where(r => r.OwnerUserId == self || r.CreatedByUserId == self);
        }

        var rules = await query.OrderBy(r => r.Name).ToListAsync(cancellationToken);
        return await MapAsync(rules, cancellationToken);
    }

    public async Task<MonitoringRuleDto> GetAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        RequireAuthor();
        var rule = await FindVisibleAsync(ruleId, cancellationToken);
        return (await MapAsync([rule], cancellationToken))[0];
    }

    public async Task<MonitoringRuleDto> CreateAsync(SaveMonitoringRuleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var organizationId = RequireAuthor();
        var owner = await ValidateAsync(command, null, cancellationToken);
        var now = _clock.UtcNow;
        var rule = new MonitoringRule { RuleId = Ids.New(), OrganizationId = organizationId, CreatedByUserId = Self };
        Apply(rule, command, owner);
        _context.MonitoringRules.Add(rule);
        AddRecipients(rule, command);
        var job = new ScheduledJob { JobId = Ids.New(), JobType = ScheduledJobType.MonitoringRule, OrganizationId = organizationId, TargetId = rule.RuleId, CreatedAt = now };
        MonitoringSchedules.Apply(job, rule.RecurrenceKind, rule.IntervalMinutes, rule.TimeOfDay, rule.DayOfWeek, await TimeZoneAsync(organizationId, cancellationToken), rule.IsActive, now);
        _context.ScheduledJobs.Add(job);
        _audit.Append(AuditEvents.MonitoringRuleSaved, "MonitoringRule", rule.RuleId.ToString(), new { created = true, kind = rule.Kind.ToString(), rule.TemplateCode, owner, severity = rule.Severity.ToString(), recipients = command.Recipients?.Count ?? 0 });
        await _context.SaveChangesAsync(cancellationToken);
        return await GetAsync(rule.RuleId, cancellationToken);
    }

    public async Task<MonitoringRuleDto> UpdateAsync(Guid ruleId, SaveMonitoringRuleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var organizationId = RequireAuthor();
        var rule = await FindEditableAsync(ruleId, cancellationToken);
        var owner = await ValidateAsync(command, ruleId, cancellationToken);
        Apply(rule, command, owner);
        var existing = await _context.MonitoringRuleRecipients.Where(r => r.RuleId == ruleId).ToListAsync(cancellationToken);
        _context.MonitoringRuleRecipients.RemoveRange(existing);
        AddRecipients(rule, command);
        await SyncJobAsync(rule, organizationId, cancellationToken);
        _audit.Append(AuditEvents.MonitoringRuleSaved, "MonitoringRule", rule.RuleId.ToString(), new { created = false, kind = rule.Kind.ToString(), rule.TemplateCode, owner, severity = rule.Severity.ToString(), recipients = command.Recipients?.Count ?? 0, rule.IsActive });
        await _context.SaveChangesAsync(cancellationToken);
        return await GetAsync(rule.RuleId, cancellationToken);
    }

    public async Task<MonitoringRuleDto> SetActiveAsync(Guid ruleId, bool isActive, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireAuthor();
        var rule = await FindEditableAsync(ruleId, cancellationToken);
        if (isActive)
        {
            // Resuming re-validates: a rule paused because the catalogue or the owner's access changed must be fixed first.
            await ValidateAsync(ToCommand(rule, await RecipientRefsAsync(ruleId, cancellationToken)), ruleId, cancellationToken);
        }

        rule.IsActive = isActive;
        if (isActive)
        {
            rule.LastError = null;
        }

        await SyncJobAsync(rule, organizationId, cancellationToken);
        _audit.Append(AuditEvents.MonitoringRuleActivation, "MonitoringRule", ruleId.ToString(), new { isActive });
        await _context.SaveChangesAsync(cancellationToken);
        return await GetAsync(ruleId, cancellationToken);
    }

    public async Task DeleteAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireAuthor();
        var rule = await FindEditableAsync(ruleId, cancellationToken);
        _context.MonitoringRuleRecipients.RemoveRange(await _context.MonitoringRuleRecipients.Where(r => r.RuleId == ruleId).ToListAsync(cancellationToken));
        _context.ScheduledJobs.RemoveRange(await _context.ScheduledJobs.Where(j => j.OrganizationId == organizationId && j.JobType == ScheduledJobType.MonitoringRule && j.TargetId == ruleId).ToListAsync(cancellationToken));
        _context.MonitoringRules.Remove(rule);
        // Alerts and evaluations stay for the retention period: the record of what the rule found outlives the rule.
        _audit.Append(AuditEvents.MonitoringRuleDeleted, "MonitoringRule", ruleId.ToString(), new { rule.Name, kind = rule.Kind.ToString() });
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<RuleTestResultDto> TestAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        RequireAuthor();
        var rule = await FindVisibleAsync(ruleId, cancellationToken);
        var output = await _runner.RunAsync(rule, RuleRunMode.Test, cancellationToken);
        _audit.Append(AuditEvents.MonitoringRuleTested, "MonitoringRule", ruleId.ToString(), new { outcome = output.Result.Outcome.ToString(), matches = output.Result.Matches.Count });
        await _context.SaveChangesAsync(cancellationToken);
        return await ToTestResultAsync(rule.Kind, RuleDefinition.Parse(rule.DefinitionJson), output.Result, cancellationToken);
    }

    public async Task<RuleTestResultDto> TestDraftAsync(SaveMonitoringRuleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var organizationId = RequireAuthor();
        var owner = await ValidateAsync(command, Guid.Empty, cancellationToken);
        var draft = new MonitoringRule { RuleId = Guid.Empty, OrganizationId = organizationId, CreatedByUserId = Self };
        Apply(draft, command, owner);
        var output = await _runner.RunAsync(draft, RuleRunMode.Draft, cancellationToken);
        return await ToTestResultAsync(draft.Kind, command.Definition, output.Result, cancellationToken);
    }

    public async Task<MonitoringSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var organizationId = RequireAdmin();
        var organization = await _context.Organizations.AsNoTracking().SingleAsync(o => o.OrganizationId == organizationId, cancellationToken);
        return ToSettings(organization);
    }

    public async Task<MonitoringSettingsDto> UpdateSettingsAsync(UpdateMonitoringSettingsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var organizationId = RequireAdmin();
        var days = command.WorkingDays?.Trim() ?? string.Empty;
        if (days.Length is 0 or > 7 || days.Any(c => c is < '1' or > '7') || days.Distinct().Count() != days.Length)
        {
            throw Invalid("settings_working_days_invalid");
        }

        if (command.WorkingHoursStart >= command.WorkingHoursEnd)
        {
            throw Invalid("settings_working_hours_invalid");
        }

        if (command.RetentionMonths < 1 || command.RetentionMonths > Math.Max(1, _options.MaxRetentionMonths))
        {
            throw Invalid("settings_retention_invalid");
        }

        var organization = await _context.Organizations.SingleAsync(o => o.OrganizationId == organizationId, cancellationToken);
        organization.MonitoringDigestTime = new TimeOnly(command.DigestTime.Hour, command.DigestTime.Minute);
        organization.WorkingHoursStart = new TimeOnly(command.WorkingHoursStart.Hour, command.WorkingHoursStart.Minute);
        organization.WorkingHoursEnd = new TimeOnly(command.WorkingHoursEnd.Hour, command.WorkingHoursEnd.Minute);
        organization.WorkingDays = new string(days.Order().ToArray());
        organization.MonitoringRetentionMonths = command.RetentionMonths;
        var now = _clock.UtcNow;
        foreach (var job in await _context.ScheduledJobs.Where(j => j.OrganizationId == organizationId && j.JobType == ScheduledJobType.AlertDigest).ToListAsync(cancellationToken))
        {
            job.TimeOfDay = organization.MonitoringDigestTime;
            job.TimeZone = organization.TimeZone;
            job.NextRunAt = JobRecurrence.NextRun(job, now);
            job.UpdatedAt = now;
        }

        _audit.Append(AuditEvents.MonitoringSettingsSaved, "Organization", organizationId.ToString(), new { digest = organization.MonitoringDigestTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), organization.WorkingDays, organization.MonitoringRetentionMonths });
        await _context.SaveChangesAsync(cancellationToken);
        return ToSettings(organization);
    }

    private MonitoringSettingsDto ToSettings(Core.Entities.Platform.Organization organization) => new(
        organization.MonitoringDigestTime, organization.WorkingHoursStart, organization.WorkingHoursEnd, organization.WorkingDays,
        RetentionPurgeJob.EffectiveRetentionMonths(organization, _options), Math.Max(1, _options.MaxRetentionMonths), organization.TimeZone);

    private async Task<Guid> ValidateAsync(SaveMonitoringRuleCommand command, Guid? ruleId, CancellationToken cancellationToken)
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        if (!IsAdmin && command.Kind == MonitoringRuleKind.Access)
        {
            throw new TenantContextException("Access rules are managed by Organization Administrators.");
        }

        var owner = ResolveOwner(command.OwnerUserId);
        if (!IsAdmin && (command.Recipients ?? []).Any(r => r.UserId != Self))
        {
            throw new TenantContextException("Management users receive their own rules only.");
        }

        var name = command.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 200 || (command.Description?.Length ?? 0) > 2000)
        {
            throw Invalid("rule_name_invalid");
        }

        if (ruleId != Guid.Empty && await _context.MonitoringRules.AnyAsync(r => r.Name == name && r.RuleId != ruleId, cancellationToken))
        {
            throw Invalid("rule_name_taken");
        }

        if (command.TemplateCode is not null && (RuleTemplates.Find(command.TemplateCode) is not { } template || template.Kind != command.Kind))
        {
            throw Invalid("rule_template_invalid");
        }

        if (!Enum.IsDefined(command.Severity) || !Enum.IsDefined(command.Kind))
        {
            throw Invalid("rule_severity_invalid");
        }

        var scheduleValid = command.RecurrenceKind switch
        {
            JobRecurrenceKind.Interval => command.IntervalMinutes is >= 15 and <= 7 * 24 * 60,
            JobRecurrenceKind.Daily => true,
            JobRecurrenceKind.Weekly => command.DayOfWeek is >= 1 and <= 7,
            _ => false,
        };
        if (!scheduleValid)
        {
            throw Invalid("rule_schedule_invalid");
        }

        if (command.CooldownHours is < 0 or > 720)
        {
            throw Invalid("rule_cooldown_invalid");
        }

        var definition = command.Definition ?? new RuleDefinition();
        var errors = RuleDefinitionValidator.Validate(command.Kind, definition);
        if (errors.Count > 0)
        {
            throw Invalid(errors[0]);
        }

        await RequireActiveUserAsync(owner, "rule_owner_invalid", cancellationToken);
        if (command.Kind == MonitoringRuleKind.Data)
        {
            var data = definition.Data!;
            if (data.Filters.Any(f => f.Values.Count > 50 || f.Values.Any(v => v is null || v.Length > 200)))
            {
                throw Invalid("rule_filter_invalid");
            }

            var permissions = await _permissions.ResolveForUserAsync(organizationId, owner, cancellationToken);
            var (resolved, error) = await RuleCatalog.ResolveAsync(_context, permissions, data, cancellationToken);
            if (resolved is null)
            {
                throw Invalid(error!);
            }

            // Dry run of the secure path: the plan must pass the shape validator and the owner's authorization now, not only at 06:00.
            foreach (var plan in DataRulePlanBuilder.Build(data, resolved.Target, DateTime.UtcNow).Plans)
            {
                if (QueryPlanValidator.Validate(plan).Count > 0)
                {
                    throw Invalid("rule_plan_invalid");
                }

                if (QueryPlanAuthorizer.Authorize(plan, resolved.Application.ApplicationId, permissions, resolved.Mappings).Plan is null)
                {
                    throw Invalid("rule_not_authorized");
                }
            }
        }
        else
        {
            var access = definition.Access!;
            var objectIds = access.ObjectIds.Distinct().ToList();
            if (objectIds.Count > 50 || await _context.DatabaseObjects.CountAsync(o => objectIds.Contains(o.ObjectId), cancellationToken) != objectIds.Count)
            {
                throw Invalid("rule_object_missing");
            }

            var codes = access.RoleCodes.Distinct(StringComparer.Ordinal).ToList();
            if (await _context.Roles.CountAsync(r => codes.Contains(r.RoleCode), cancellationToken) != codes.Count)
            {
                throw Invalid("rule_role_invalid");
            }
        }

        var recipients = command.Recipients ?? [];
        if (recipients.Count > 50 || recipients.Any(r => (r.UserId is null) == (r.RoleId is null)))
        {
            throw Invalid("rule_recipients_invalid");
        }

        var userIds = recipients.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).Distinct().ToList();
        var roleIds = recipients.Where(r => r.RoleId is not null).Select(r => r.RoleId!.Value).Distinct().ToList();
        if (await _context.OrganizationUsers.CountAsync(u => userIds.Contains(u.UserId) && u.Status == OrganizationUserStatus.Active, cancellationToken) != userIds.Count
            || await _context.Roles.CountAsync(r => roleIds.Contains(r.RoleId), cancellationToken) != roleIds.Count)
        {
            throw Invalid("rule_recipients_invalid");
        }

        return owner;
    }

    private static void Apply(MonitoringRule rule, SaveMonitoringRuleCommand command, Guid owner)
    {
        rule.Name = command.Name.Trim();
        rule.Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim();
        rule.Kind = command.Kind;
        rule.TemplateCode = command.TemplateCode;
        rule.DefinitionJson = (command.Definition ?? new RuleDefinition()).Serialize();
        rule.Severity = command.Severity;
        rule.RecurrenceKind = command.RecurrenceKind;
        rule.IntervalMinutes = command.RecurrenceKind == JobRecurrenceKind.Interval ? command.IntervalMinutes : 0;
        rule.TimeOfDay = new TimeOnly(command.TimeOfDay.Hour, command.TimeOfDay.Minute);
        rule.DayOfWeek = command.RecurrenceKind == JobRecurrenceKind.Weekly ? command.DayOfWeek : null;
        rule.CooldownHours = command.CooldownHours;
        rule.IsActive = command.IsActive;
        rule.OwnerUserId = owner;
    }

    private void AddRecipients(MonitoringRule rule, SaveMonitoringRuleCommand command)
    {
        var seen = new HashSet<(Guid?, Guid?)>();
        foreach (var recipient in command.Recipients ?? [])
        {
            if (seen.Add((recipient.UserId, recipient.RoleId)) && recipient.UserId != rule.OwnerUserId)
            {
                _context.MonitoringRuleRecipients.Add(new MonitoringRuleRecipient { RecipientId = Ids.New(), OrganizationId = rule.OrganizationId, RuleId = rule.RuleId, UserId = recipient.UserId, RoleId = recipient.RoleId });
            }
        }
    }

    private async Task SyncJobAsync(MonitoringRule rule, Guid organizationId, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var job = await _context.ScheduledJobs.FirstOrDefaultAsync(j => j.OrganizationId == organizationId && j.JobType == ScheduledJobType.MonitoringRule && j.TargetId == rule.RuleId, cancellationToken);
        if (job is null)
        {
            job = new ScheduledJob { JobId = Ids.New(), JobType = ScheduledJobType.MonitoringRule, OrganizationId = organizationId, TargetId = rule.RuleId, CreatedAt = now };
            _context.ScheduledJobs.Add(job);
        }

        MonitoringSchedules.Apply(job, rule.RecurrenceKind, rule.IntervalMinutes, rule.TimeOfDay, rule.DayOfWeek, await TimeZoneAsync(organizationId, cancellationToken), rule.IsActive, now);
    }

    private async Task<string> TimeZoneAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await _context.Organizations.AsNoTracking().Where(o => o.OrganizationId == organizationId).Select(o => o.TimeZone).SingleAsync(cancellationToken);

    private async Task<RuleTestResultDto> ToTestResultAsync(MonitoringRuleKind kind, RuleDefinition definition, RuleRunResult result, CancellationToken cancellationToken)
    {
        if (kind == MonitoringRuleKind.Data && definition.Data is { } data && result.Matches.Count > 0)
        {
            // The caller sees values only if their own permissions cover them (an administrator testing someone else's rule usually does not).
            var viewer = await _permissions.ResolveForUserAsync(_tenantContext.RequireOrganizationId(), Self, cancellationToken);
            if (!RuleCatalog.CanSeeDetails(viewer, data))
            {
                return new RuleTestResultDto(result.Outcome, result.Matches.Count, result.RowsExamined, result.ErrorCode, true, []);
            }
        }

        var subjects = result.Matches.Where(m => m.SubjectUserId is not null).Select(m => m.SubjectUserId!.Value).ToList();
        var names = await _context.OrganizationUsers.AsNoTracking().Where(u => subjects.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.FullName, cancellationToken);
        var matches = result.Matches.Take(50).Select(m => new RuleMatchDto(
            m.SubjectUserId is null ? m.GroupKey : null, m.SubjectUserId is null ? m.GroupValues : null, m.Measure, m.Previous, m.Threshold, m.SubjectUserId, m.SubjectUserId is { } s ? names.GetValueOrDefault(s) : null)).ToList();
        return new RuleTestResultDto(result.Outcome, result.Matches.Count, result.RowsExamined, result.ErrorCode, false, matches);
    }

    private async Task<IReadOnlyList<MonitoringRuleDto>> MapAsync(IReadOnlyList<MonitoringRule> rules, CancellationToken cancellationToken)
    {
        var ruleIds = rules.Select(r => r.RuleId).ToList();
        var recipients = await _context.MonitoringRuleRecipients.AsNoTracking().Where(r => ruleIds.Contains(r.RuleId)).ToListAsync(cancellationToken);
        var userIds = rules.Select(r => r.OwnerUserId).Concat(recipients.Where(r => r.UserId != null).Select(r => r.UserId!.Value)).Distinct().ToList();
        var users = await _context.OrganizationUsers.AsNoTracking().Where(u => userIds.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.FullName, cancellationToken);
        var roles = await _context.Roles.AsNoTracking().ToDictionaryAsync(r => r.RoleId, r => r.NameEn, cancellationToken);
        var definitions = rules.ToDictionary(r => r.RuleId, r => RuleDefinition.Parse(r.DefinitionJson));
        var objectIds = definitions.Values.Where(d => d.Data is not null).Select(d => d.Data!.ObjectId).Distinct().ToList();
        var objects = await _context.DatabaseObjects.AsNoTracking().Where(o => objectIds.Contains(o.ObjectId)).ToDictionaryAsync(o => o.ObjectId, o => $"{o.SchemaName}.{o.ObjectName}", cancellationToken);
        var jobs = await _context.ScheduledJobs.AsNoTracking().Where(j => j.JobType == ScheduledJobType.MonitoringRule && j.TargetId != null && ruleIds.Contains(j.TargetId.Value)).ToListAsync(cancellationToken);
        var open = await _context.Alerts.AsNoTracking().Where(a => ruleIds.Contains(a.RuleId) && (a.Status == AlertStatus.Open || a.Status == AlertStatus.Acknowledged)).GroupBy(a => a.RuleId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return rules.Select(r =>
        {
            var definition = definitions[r.RuleId];
            var job = jobs.FirstOrDefault(j => j.TargetId == r.RuleId);
            return new MonitoringRuleDto(
                r.RuleId, r.Name, r.Description, r.Kind, r.TemplateCode, definition, r.Severity, r.RecurrenceKind, r.IntervalMinutes, r.TimeOfDay, r.DayOfWeek, r.CooldownHours, r.IsActive,
                r.OwnerUserId, users.GetValueOrDefault(r.OwnerUserId) ?? string.Empty, r.CreatedByUserId,
                recipients.Where(x => x.RuleId == r.RuleId).Select(x => new RuleRecipientDto(x.UserId, x.RoleId, x.UserId is { } u ? users.GetValueOrDefault(u) ?? string.Empty : roles.GetValueOrDefault(x.RoleId!.Value) ?? string.Empty)).ToList(),
                definition.Data is { } d ? objects.GetValueOrDefault(d.ObjectId) : null,
                r.LastEvaluatedAt, r.LastOutcome, r.LastError, job is { IsEnabled: true } ? job.NextRunAt : null, open.GetValueOrDefault(r.RuleId), CanEdit(r), r.CreatedAt, r.UpdatedAt);
        }).ToList();
    }

    private async Task<IReadOnlyList<RuleRecipientRef>> RecipientRefsAsync(Guid ruleId, CancellationToken cancellationToken) =>
        await _context.MonitoringRuleRecipients.AsNoTracking().Where(r => r.RuleId == ruleId).Select(r => new RuleRecipientRef(r.UserId, r.RoleId)).ToListAsync(cancellationToken);

    private static SaveMonitoringRuleCommand ToCommand(MonitoringRule rule, IReadOnlyList<RuleRecipientRef> recipients) => new(
        rule.Name, rule.Description, rule.Kind, rule.TemplateCode, RuleDefinition.Parse(rule.DefinitionJson), rule.Severity, rule.RecurrenceKind, rule.IntervalMinutes, rule.TimeOfDay, rule.DayOfWeek, rule.CooldownHours, true, rule.OwnerUserId, recipients);

    private bool CanEdit(MonitoringRule rule) => IsAdmin || (IsManagement && rule.CreatedByUserId == Self && rule.OwnerUserId == Self);

    private async Task<MonitoringRule> FindVisibleAsync(Guid ruleId, CancellationToken cancellationToken)
    {
        var rule = await _context.MonitoringRules.SingleOrDefaultAsync(r => r.RuleId == ruleId, cancellationToken);
        if (rule is null || !(IsAdmin || rule.OwnerUserId == Self || rule.CreatedByUserId == Self))
        {
            throw new DomainException(OrganizationOperationOutcome.NotFound, "rule_not_found");
        }

        return rule;
    }

    private async Task<MonitoringRule> FindEditableAsync(Guid ruleId, CancellationToken cancellationToken)
    {
        var rule = await FindVisibleAsync(ruleId, cancellationToken);
        if (!CanEdit(rule))
        {
            throw new TenantContextException("Only the rule's author or an Organization Administrator can change it.");
        }

        return rule;
    }

    private Guid ResolveOwner(Guid? requested)
    {
        if (IsAdmin)
        {
            return requested ?? Self;
        }

        if (requested is { } r && r != Self)
        {
            throw new TenantContextException("Management users own their rules.");
        }

        return Self;
    }

    private async Task RequireActiveUserAsync(Guid userId, string code, CancellationToken cancellationToken)
    {
        if (!await _context.OrganizationUsers.AnyAsync(u => u.UserId == userId && u.Status == OrganizationUserStatus.Active, cancellationToken))
        {
            throw Invalid(code);
        }
    }

    private Guid RequireAuthor()
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        if (_tenantContext.Principal.PrincipalType != PrincipalType.OrganizationUser || !(IsAdmin || IsManagement))
        {
            throw new TenantContextException("Monitoring Rules are managed by Organization Administrators and Management users.");
        }

        return organizationId;
    }

    private Guid RequireAdmin()
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        if (!IsAdmin)
        {
            throw new TenantContextException("Monitoring settings are managed by Organization Administrators.");
        }

        return organizationId;
    }

    private static DomainException Invalid(string code) => new(OrganizationOperationOutcome.ValidationFailed, code);
}
