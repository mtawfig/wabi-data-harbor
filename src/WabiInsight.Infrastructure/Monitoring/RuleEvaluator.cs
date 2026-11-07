using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Ai.Compilers;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Monitoring;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Monitoring;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Permissions;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Ai;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Monitoring;

/// <summary>One group that met a rule's condition, before any viewer-specific redaction.</summary>
public sealed record RuleMatch(string GroupKey, IReadOnlyList<string?> GroupValues, decimal Measure, decimal? Previous, decimal? Threshold, Guid? SubjectUserId, IReadOnlyDictionary<string, object?> Summary);

/// <summary><see cref="Invalid"/> means the rule can no longer run as defined (catalogue changed, owner lost access or left); the scheduled path pauses it.</summary>
public sealed record RuleRunResult(RuleEvaluationOutcome Outcome, IReadOnlyList<RuleMatch> Matches, long RowsExamined, string? ErrorCode, bool Invalid)
{
    public static RuleRunResult Fail(string code, bool invalid = false) => new(RuleEvaluationOutcome.Failed, [], 0, code, invalid);
}

/// <summary>A data rule resolved against the catalogue and the owner's effective permissions.</summary>
public sealed record ResolvedDataRule(RuleObjectInfo Target, Core.Entities.Organizations.Application Application, IReadOnlyDictionary<Guid, RowMappingColumns> Mappings);

/// <summary>
/// Catalogue and permission checks shared by save-time validation and every evaluation (specification §35.1):
/// the object and every referenced column must be present, approved and permitted to the owner.
/// </summary>
public static class RuleCatalog
{
    public static async Task<(ResolvedDataRule? Rule, string? Error)> ResolveAsync(ConfigurationDbContext context, EffectivePermissions permissions, DataRuleDefinition rule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(rule);
        var organizationId = permissions.OrganizationId;
        var row = await context.DatabaseObjects.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(o => o.OrganizationId == organizationId && o.ObjectId == rule.ObjectId, cancellationToken);
        if (row is null)
        {
            return (null, "rule_object_missing");
        }

        if (!row.IsApproved || !row.IsPresent)
        {
            return (null, "rule_object_unavailable");
        }

        var application = await context.Applications.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(a => a.OrganizationId == organizationId && a.ApplicationId == row.ApplicationId, cancellationToken);
        if (application is null || !application.IsApproved || !application.IsActive)
        {
            return (null, "rule_application_unavailable");
        }

        if (application.IsBuiltIn)
        {
            return (null, "rule_builtin_not_supported");
        }

        var access = permissions.Objects.FirstOrDefault(o => o.ObjectId == rule.ObjectId && o.YieldsRows);
        if (access is null)
        {
            return (null, "rule_not_authorized");
        }

        var referenced = rule.GroupByColumnIds
            .Concat(rule.Filters.Select(f => f.ColumnId))
            .Concat(rule.TimeColumnId is { } t ? [t] : [])
            .Concat(rule.Measure.ColumnId is { } m ? [m] : [])
            .Distinct()
            .ToList();
        var catalogued = await context.DatabaseColumns.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && c.ObjectId == rule.ObjectId && referenced.Contains(c.ColumnId))
            .ToDictionaryAsync(c => c.ColumnId, cancellationToken);
        var permitted = access.Columns.ToDictionary(c => c.ColumnId);
        foreach (var columnId in referenced)
        {
            if (!catalogued.TryGetValue(columnId, out var column) || !column.IsPresent || !column.IsApproved)
            {
                return (null, "rule_column_unavailable");
            }

            if (!permitted.ContainsKey(columnId))
            {
                return (null, "rule_not_authorized");
            }
        }

        var columns = access.Columns.ToDictionary(c => c.ColumnId, c => (c.ColumnName, c.DataType));
        var mappingRows = await context.RowScopeColumnMappings.IgnoreQueryFilters().AsNoTracking().Where(m => m.OrganizationId == organizationId && m.ObjectId == rule.ObjectId).ToListAsync(cancellationToken);
        var allColumns = await context.DatabaseColumns.IgnoreQueryFilters().AsNoTracking().Where(c => c.OrganizationId == organizationId && c.ObjectId == rule.ObjectId).ToDictionaryAsync(c => c.ColumnId, c => c.ColumnName, cancellationToken);
        var mappings = mappingRows.ToDictionary(m => m.ObjectId, m => new RowMappingColumns(
            m.EmployeeColumnId is { } e ? allColumns.GetValueOrDefault(e) : null,
            m.DepartmentColumnId is { } d ? allColumns.GetValueOrDefault(d) : null,
            m.SectionColumnId is { } s ? allColumns.GetValueOrDefault(s) : null));
        return (new ResolvedDataRule(new RuleObjectInfo(row.ObjectId, row.ApplicationId, row.SchemaName, row.ObjectName, columns), application, mappings), null);
    }

    /// <summary>True when the viewer may read the rule's object and every column whose values appear in a match (group-by and measure).</summary>
    public static bool CanSeeDetails(EffectivePermissions viewer, DataRuleDefinition rule)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(rule);
        var access = viewer.Objects.FirstOrDefault(o => o.ObjectId == rule.ObjectId && o.YieldsRows);
        if (access is null)
        {
            return false;
        }

        var permitted = access.Columns.Select(c => c.ColumnId).ToHashSet();
        return rule.GroupByColumnIds.All(permitted.Contains) && (rule.Measure.ColumnId is not { } m || permitted.Contains(m));
    }
}

/// <summary>
/// Evaluates one rule. Must run in a scope whose principal is the rule's <b>owner</b>: data rules go through the
/// same validator, authorizer, dialect compiler and read-only executor as the owner's own questions, so row
/// scopes and column grants apply and nothing is widened. Access rules read the organization's ledger.
/// </summary>
public sealed class RuleEvaluator
{
    public const int MaxMatches = 200;

    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IPermissionResolver _permissions;
    private readonly ISqlDialectCompilerFactory _compilers;
    private readonly IQueryExecutor _executor;
    private readonly IClock _clock;

    public RuleEvaluator(ConfigurationDbContext context, ITenantContext tenantContext, IPermissionResolver permissions, ISqlDialectCompilerFactory compilers, IQueryExecutor executor, IClock clock)
    {
        _context = context;
        _tenantContext = tenantContext;
        _permissions = permissions;
        _compilers = compilers;
        _executor = executor;
        _clock = clock;
    }

    public async Task<RuleRunResult> EvaluateAsync(MonitoringRuleKind kind, RuleDefinition definition, Organization organization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(organization);
        if (_tenantContext.Principal.PrincipalType != PrincipalType.OrganizationUser || _tenantContext.OrganizationId != organization.OrganizationId)
        {
            throw new TenantContextException("Rules evaluate as their owner.");
        }

        if (RuleDefinitionValidator.Validate(kind, definition).Count > 0)
        {
            return RuleRunResult.Fail("rule_definition_invalid", invalid: true);
        }

        return kind == MonitoringRuleKind.Data
            ? await EvaluateDataAsync(definition.Data!, organization, cancellationToken)
            : await EvaluateAccessAsync(definition.Access!, organization, cancellationToken);
    }

    private async Task<RuleRunResult> EvaluateDataAsync(DataRuleDefinition rule, Organization organization, CancellationToken cancellationToken)
    {
        var permissions = await _permissions.ResolveAsync(cancellationToken);
        var (resolved, error) = await RuleCatalog.ResolveAsync(_context, permissions, rule, cancellationToken);
        if (resolved is null)
        {
            // An application that is not (yet) approved or connected is an administrator's pending step, not a broken rule: fail this run, keep the rule.
            return RuleRunResult.Fail(error!, invalid: error != "rule_application_unavailable");
        }

        var zone = JobRecurrence.ResolveTimeZone(organization.TimeZone);
        var nowLocal = TimeZoneInfo.ConvertTime(_clock.UtcNow, zone).DateTime;
        var plans = DataRulePlanBuilder.Build(rule, resolved.Target, nowLocal);
        var compiler = _compilers.Resolve(resolved.Application.DatabaseType);
        var results = new List<AnalysisResult>(plans.Plans.Count);
        try
        {
            foreach (var plan in plans.Plans)
            {
                if (QueryPlanValidator.Validate(plan).Count > 0)
                {
                    return RuleRunResult.Fail("rule_plan_invalid", invalid: true);
                }

                var (authorized, _) = QueryPlanAuthorizer.Authorize(plan, resolved.Application.ApplicationId, permissions, resolved.Mappings);
                if (authorized is null)
                {
                    return RuleRunResult.Fail("rule_not_authorized", invalid: true);
                }

                var compiled = compiler.Compile(authorized);
                results.Add(await _executor.ExecuteAsync(resolved.Application.ApplicationId, compiled, plan.Visualization, cancellationToken));
            }
        }
        catch (DomainException ex)
        {
            return RuleRunResult.Fail(ex.Message);
        }
        catch (AnalysisExecutionException ex)
        {
            return RuleRunResult.Fail(ex.Code);
        }

        var rowsExamined = results.Sum(r => (long)r.RowCount);
        var current = results[0];
        var measureIndex = IndexOf(current, plans.MeasureAlias);
        var matches = new List<RuleMatch>();
        if (rule.Comparison == "previous_period")
        {
            var now = current.Rows.Count > 0 ? ToDecimal(current.Rows[0][measureIndex]) : 0m;
            var previous = results[1].Rows.Count > 0 ? ToDecimal(results[1].Rows[0][IndexOf(results[1], plans.MeasureAlias)]) : 0m;
            var change = previous == 0 ? (now == 0 ? 0 : 100) : Math.Round((now - previous) / Math.Abs(previous) * 100m, 2);
            if (rule.Condition.Matches(change))
            {
                matches.Add(new RuleMatch("*", [], now, previous, rule.Condition.Value, null, new Dictionary<string, object?>
                {
                    ["measure"] = now, ["previous"] = previous, ["changePercent"] = change, ["operator"] = rule.Condition.Operator, ["threshold"] = rule.Condition.Value, ["windowHours"] = rule.WindowHours,
                }));
            }
        }
        else
        {
            var groupIndexes = plans.GroupAliases.Select(a => IndexOf(current, a)).ToList();
            foreach (var row in current.Rows)
            {
                var measure = ToDecimal(row[measureIndex]);
                if (!rule.Condition.Matches(measure))
                {
                    continue;
                }

                var values = groupIndexes.Select(i => Format(row[i])).ToList();
                var key = values.Count == 0 ? "*" : string.Join(" | ", values.Select(v => v ?? "∅"));
                matches.Add(new RuleMatch(key.Length <= 400 ? key : key[..400], values, measure, null, rule.Condition.Value, null, new Dictionary<string, object?>
                {
                    ["measure"] = measure, ["operator"] = rule.Condition.Operator, ["threshold"] = rule.Condition.Value, ["windowHours"] = rule.WindowHours, ["groupValues"] = values,
                }));
                if (matches.Count >= MaxMatches)
                {
                    break;
                }
            }
        }

        return new RuleRunResult(matches.Count > 0 ? RuleEvaluationOutcome.Fired : RuleEvaluationOutcome.NoMatch, matches, rowsExamined, null, false);
    }

    private async Task<RuleRunResult> EvaluateAccessAsync(AccessRuleDefinition rule, Organization organization, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var start = now.AddHours(-rule.WindowHours);
        var query = _context.AnalysisAccessFacts.AsNoTracking().Where(f => f.AccessedAt >= start && f.AccessedAt <= now);
        if (rule.ObjectIds.Count > 0)
        {
            var ids = rule.ObjectIds.Cast<Guid?>().ToList();
            query = query.Where(f => ids.Contains(f.ObjectId));
        }

        var facts = await query
            .Select(f => new { f.FactId, f.UserId, f.AccessedAt, f.MetricId, f.HistoryVersionId, f.RowCount, f.HighestClassification, f.IsExport, f.Outcome, f.RoleCodes, f.IdentifierFilterValueCount })
            .ToListAsync(cancellationToken);

        var zone = JobRecurrence.ResolveTimeZone(organization.TimeZone);
        var workingDays = organization.WorkingDays ?? string.Empty;
        HashSet<Guid>? newUsers = null;
        if (rule.NewUsersWithinDays is { } days)
        {
            var since = now.AddDays(-days);
            newUsers = (await _context.OrganizationUsers.AsNoTracking().Where(u => u.CreatedAt >= since).Select(u => u.UserId).ToListAsync(cancellationToken)).ToHashSet();
        }

        bool OutsideHours(DateTimeOffset at)
        {
            var local = TimeZoneInfo.ConvertTime(at, zone);
            var day = JobRecurrence.IsoDayOfWeek(DateOnly.FromDateTime(local.DateTime)).ToString(CultureInfo.InvariantCulture);
            var time = TimeOnly.FromDateTime(local.DateTime);
            return !workingDays.Contains(day, StringComparison.Ordinal) || time < organization.WorkingHoursStart || time >= organization.WorkingHoursEnd;
        }

        var selected = facts
            .Where(f => rule.MinClassification is not { } min || f.HighestClassification >= min)
            .Where(f => rule.RoleCodes.Count == 0 || f.RoleCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Intersect(rule.RoleCodes, StringComparer.Ordinal).Any())
            .Where(f => !rule.OutsideWorkingHours || OutsideHours(f.AccessedAt))
            .Where(f => newUsers is null || newUsers.Contains(f.UserId))
            .ToList();

        var perUser = selected.GroupBy(f => f.UserId).Select(g =>
        {
            var succeeded = g.Where(f => f.Outcome == AccessOutcome.Succeeded && !f.IsExport).ToList();
            decimal value = rule.Signal switch
            {
                "analyses" => succeeded.Select(f => f.MetricId ?? f.HistoryVersionId ?? f.FactId).Distinct().Count(),
                "rows" => succeeded.Sum(f => (long)f.RowCount),
                "restricted_touches" => succeeded.Count(f => f.HighestClassification == DataClassification.Restricted),
                "exports" => g.Where(f => f.IsExport).Select(f => f.HistoryVersionId ?? f.FactId).Distinct().Count(),
                "unauthorized" => g.Where(f => f.Outcome == AccessOutcome.Unauthorized).Select(f => f.MetricId ?? f.FactId).Distinct().Count(),
                "identifier_filter_values" => succeeded.Sum(f => (long)f.IdentifierFilterValueCount),
                _ => g.Where(f => f.Outcome == AccessOutcome.Succeeded).Select(f => f.MetricId ?? f.HistoryVersionId ?? f.FactId).Distinct().Count(),
            };
            var roles = g.Select(f => f.RoleCodes).Where(r => r.Length > 0).OrderByDescending(r => r.Length).FirstOrDefault() ?? string.Empty;
            return (UserId: g.Key, Value: value, Roles: roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }).ToList();

        Dictionary<Guid, AccessBaseline> userBaselines = [];
        Dictionary<string, AccessBaseline> roleBaselines = [];
        if (rule.Threshold.Mode == "baseline")
        {
            var baselines = await _context.AccessBaselines.AsNoTracking().ToListAsync(cancellationToken);
            userBaselines = baselines.Where(b => b.SubjectKind == BaselineSubjectKind.User && b.UserId is not null).GroupBy(b => b.UserId!.Value).ToDictionary(g => g.Key, g => g.First());
            roleBaselines = baselines.Where(b => b.SubjectKind == BaselineSubjectKind.Role && b.RoleCode is not null).GroupBy(b => b.RoleCode!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        }

        var matches = new List<RuleMatch>();
        foreach (var user in perUser.OrderByDescending(u => u.Value))
        {
            decimal limit;
            string basis = "absolute";
            decimal? expected = null;
            if (rule.Threshold.Mode == "baseline")
            {
                var baseline = userBaselines.GetValueOrDefault(user.UserId) is { } own && own.ActiveDays >= rule.Threshold.MinActiveDays ? own : null;
                basis = "user";
                if (baseline is null)
                {
                    baseline = user.Roles.Select(r => roleBaselines.GetValueOrDefault(r)).FirstOrDefault(b => b is not null && b.ActiveDays >= rule.Threshold.MinActiveDays);
                    basis = "role";
                }

                if (baseline is null)
                {
                    continue; // Immature baseline: never compared (specification §35.3).
                }

                expected = Math.Round((decimal)Statistic(baseline, rule.Signal, rule.Threshold.Statistic) * rule.WindowHours / 24m, 2);
                limit = Math.Max(rule.Threshold.Value * expected.Value, rule.Threshold.Minimum);
                if (user.Value < rule.Threshold.Minimum)
                {
                    continue;
                }
            }
            else
            {
                limit = rule.Threshold.Value;
            }

            if (user.Value <= limit)
            {
                continue;
            }

            matches.Add(new RuleMatch(user.UserId.ToString(), [], user.Value, null, limit, user.UserId, new Dictionary<string, object?>
            {
                ["signal"] = rule.Signal, ["measure"] = user.Value, ["threshold"] = limit, ["basis"] = basis, ["expected"] = expected, ["multiplier"] = rule.Threshold.Mode == "baseline" ? rule.Threshold.Value : null, ["windowHours"] = rule.WindowHours,
            }));
            if (matches.Count >= MaxMatches)
            {
                break;
            }
        }

        return new RuleRunResult(matches.Count > 0 ? RuleEvaluationOutcome.Fired : RuleEvaluationOutcome.NoMatch, matches, selected.Count, null, false);
    }

    private static double Statistic(AccessBaseline b, string signal, string statistic)
    {
        var p95 = statistic == "p95";
        return signal switch
        {
            "analyses" => p95 ? b.QueriesPerDayP95 : b.QueriesPerDayMedian,
            "rows" => p95 ? b.RowsPerDayP95 : b.RowsPerDayMedian,
            "restricted_touches" => p95 ? b.RestrictedTouchesPerDayP95 : b.RestrictedTouchesPerDayMedian,
            "exports" => p95 ? b.ExportsPerDayP95 : b.ExportsPerDayMedian,
            "unauthorized" => p95 ? b.UnauthorizedPerDayP95 : b.UnauthorizedPerDayMedian,
            _ => 0,
        };
    }

    private static int IndexOf(AnalysisResult result, string alias)
    {
        for (var i = 0; i < result.Columns.Count; i++)
        {
            if (string.Equals(result.Columns[i].Name, alias, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw new InvalidOperationException($"Result has no column '{alias}'.");
    }

    internal static decimal ToDecimal(object? value) => value switch
    {
        null or DBNull => 0m,
        decimal d => d,
        IConvertible c => c.ToDecimal(CultureInfo.InvariantCulture),
        _ => 0m,
    };

    internal static string? Format(object? value) => value switch
    {
        null or DBNull => null,
        DateTime d => d.ToString(d.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
