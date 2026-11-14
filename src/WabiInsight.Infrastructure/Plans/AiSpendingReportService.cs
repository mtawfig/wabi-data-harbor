using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Ai;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Monitoring;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Plans;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Plans;

/// <summary>
/// The Organization Administrator's AI spending report (P13.47; money and tokens from the AI usage ledger since P13.48). Everything is read through the tenant filter of the
/// signed-in administrator's organization and again by its id: the organization never comes from the request. The
/// report carries amounts and counts only, never a question or a row of data.
/// </summary>
public sealed class AiSpendingReportService : IAiSpendingReportService
{
    /// <summary>Longest period the month-by-month view covers.</summary>
    public const int MaxMonths = 24;

    private const int DefaultMonths = 6;

    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public AiSpendingReportService(ConfigurationDbContext context, ITenantContext tenantContext, IClock clock)
    {
        _context = context;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<AiSpendingReportDto> GetAsync(AiSpendingReportFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var organizationId = _tenantContext.RequireOrganizationId();
        if (!_tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin))
        {
            throw new TenantContextException("Only organization administrators see the AI spending report.");
        }

        if (filter.MinSpend is < 0 || filter.MaxSpend is < 0 || (filter.MinSpend is { } min && filter.MaxSpend is { } max && min > max))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "spending_range_invalid");
        }

        var organization = await _context.Organizations.AsNoTracking().SingleAsync(o => o.OrganizationId == organizationId, cancellationToken);
        var zone = JobRecurrence.ResolveTimeZone(organization.TimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.UtcNow, zone).DateTime);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        // The table: the dates asked for, or the current month.
        var from = filter.From ?? (filter.To is { } onlyTo ? new DateOnly(onlyTo.Year, onlyTo.Month, 1) : monthStart);
        var to = filter.To ?? (filter.From is null ? today : filter.From.Value > today ? filter.From.Value : today);
        if (from > to)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "spending_dates_invalid");
        }

        // Month by month: the months of the dates asked for, or the last six; never more than two years.
        var firstMonth = filter.From is null && filter.To is null ? monthStart.AddMonths(-(DefaultMonths - 1)) : new DateOnly(from.Year, from.Month, 1);
        var lastMonth = new DateOnly(to.Year, to.Month, 1);
        if (Months(firstMonth, lastMonth) > MaxMonths)
        {
            firstMonth = lastMonth.AddMonths(-(MaxMonths - 1));
        }

        var readFrom = firstMonth < from ? firstMonth : from;
        var readTo = lastMonth.AddMonths(1).AddDays(-1) > to ? lastMonth.AddMonths(1).AddDays(-1) : to;
        var startUtc = StartOfDay(readFrom, zone);
        var endUtc = StartOfDay(readTo.AddDays(1), zone);

        // Questions come from the per-question records (a refused question is a request that cost nothing); money and
        // tokens come from the AI usage ledger, which also holds the calls made outside a question (P13.48).
        var runs = await _context.AnalysisExecutionMetrics.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && m.CreatedAt >= startUtc && m.CreatedAt < endUtc)
            .Select(m => new { m.MetricId, m.UserId, m.CreatedAt, m.Status }).ToListAsync(cancellationToken);
        var calls = await _context.AiCalls.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && c.CreatedAt >= startUtc && c.CreatedAt < endUtc)
            .Select(c => new { c.UserId, c.CreatedAt, c.RunId, c.Succeeded, c.TotalCost, c.InputTokens, c.OutputTokens }).ToListAsync(cancellationToken);

        // A call's question may have started just before the period: its outcome is read by id.
        var statuses = runs.ToDictionary(r => r.MetricId, r => r.Status);
        var outside = calls.Where(c => c.RunId is { } run && !statuses.ContainsKey(run)).Select(c => c.RunId!.Value).Distinct().ToList();
        if (outside.Count > 0)
        {
            foreach (var run in await _context.AnalysisExecutionMetrics.AsNoTracking().Where(m => m.OrganizationId == organizationId && outside.Contains(m.MetricId)).Select(m => new { m.MetricId, m.Status }).ToListAsync(cancellationToken))
            {
                statuses[run.MetricId] = run.Status;
            }
        }

        DateOnly DayOf(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);

        var local = runs
            .Where(r => !filter.SuccessfulOnly || r.Status == AnalysisStatus.Completed)
            .Select(r => new SpendItem(r.UserId, DayOf(r.CreatedAt), 1, 0m, 0, 0))
            .Concat(calls
                .Where(c => !filter.SuccessfulOnly || (c.RunId is { } run ? statuses.TryGetValue(run, out var status) && status == AnalysisStatus.Completed : c.Succeeded))
                // A call outside a question (a rule draft, a provider test) is a request of its own.
                .Select(c => new SpendItem(c.UserId ?? Guid.Empty, DayOf(c.CreatedAt), c.RunId is null ? 1 : 0, c.TotalCost, c.InputTokens, c.OutputTokens)))
            .Where(i => filter.UserId is not { } userId || i.UserId == userId)
            .ToList();

        var users = await _context.OrganizationUsers.AsNoTracking().Where(u => u.OrganizationId == organizationId)
            .Select(u => new { u.UserId, u.FullName, u.EmployeeNumber, u.DepartmentId, u.Status }).ToListAsync(cancellationToken);
        var departments = await _context.Departments.AsNoTracking().Where(d => d.OrganizationId == organizationId)
            .ToDictionaryAsync(d => d.DepartmentId, d => new { d.DepartmentNameEn, d.DepartmentNameAr }, cancellationToken);
        var byId = users.ToDictionary(u => u.UserId);

        bool Within(decimal spend) => (filter.MinSpend is not { } low || spend >= low) && (filter.MaxSpend is not { } high || spend <= high);

        var perUser = local.Where(m => m.Day >= from && m.Day <= to)
            .GroupBy(m => m.UserId)
            .Select(g => new { UserId = g.Key, Requests = g.Sum(m => m.Requests), Spend = g.Sum(m => m.Cost), InputTokens = g.Sum(m => m.InputTokens), OutputTokens = g.Sum(m => m.OutputTokens) })
            .Where(g => Within(g.Spend))
            .OrderByDescending(g => g.Spend).ThenBy(g => byId.TryGetValue(g.UserId, out var u) ? u.FullName : string.Empty, StringComparer.Ordinal)
            .ToList();
        var totalSpend = perUser.Sum(g => g.Spend);
        var rows = perUser.Select(g =>
        {
            byId.TryGetValue(g.UserId, out var user);
            var department = user?.DepartmentId is { } departmentId && departments.TryGetValue(departmentId, out var d) ? d : null;
            return new AiSpendingUserRow(g.UserId, user?.FullName ?? string.Empty, user?.EmployeeNumber, department?.DepartmentNameEn, department?.DepartmentNameAr,
                user?.Status == OrganizationUserStatus.Active, g.Requests, Math.Round(g.Spend, 2), totalSpend > 0 ? Math.Round(g.Spend / totalSpend * 100m, 1) : 0m, g.InputTokens, g.OutputTokens);
        }).ToList();

        var months = Enumerable.Range(0, Months(firstMonth, lastMonth)).Select(i => firstMonth.AddMonths(i)).ToList();
        var monthly = local.Where(m => m.Day >= firstMonth && m.Day < lastMonth.AddMonths(1))
            .GroupBy(m => m.UserId)
            .Select(g =>
            {
                var values = months.Select(month => Math.Round(g.Where(m => m.Day.Year == month.Year && m.Day.Month == month.Month).Sum(m => m.Cost), 2)).ToList();
                return new AiSpendingMonthlyRow(g.Key, byId.TryGetValue(g.Key, out var user) ? user.FullName : string.Empty, values, values.Sum());
            })
            .Where(r => Within(r.Total))
            .OrderByDescending(r => r.Total).ThenBy(r => r.FullName, StringComparer.Ordinal)
            .ToList();
        var monthlyTotals = months.Select((_, index) => monthly.Sum(r => r.Spend[index])).ToList();

        // The month's position against the plan is always the whole organization's, whatever the filters say.
        var plan = organization.ServicePlanId is { } planId ? await _context.ServicePlans.AsNoTracking().SingleOrDefaultAsync(p => p.PlanId == planId, cancellationToken) : null;
        var monthSpend = await Monitoring.AiSpend.MonthToDateAsync(_context.AiCalls, organization, _clock.UtcNow, cancellationToken);
        decimal? percent = plan?.MonthlyAiSpendLimit is { } limit && limit > 0 ? Math.Round(monthSpend / limit * 100m, 1) : null;

        return new AiSpendingReportDto(
            AiSpendingState.Of(plan?.MonthlyAiSpendLimit, monthSpend), plan?.PlanCode, plan?.NameEn, plan?.NameAr, plan?.MonthlyAiSpendLimit, Math.Round(monthSpend, 2), percent,
            from, to, rows.Sum(r => r.Requests), Math.Round(totalSpend, 2), rows,
            months.Select(m => m.ToString("yyyy-MM", CultureInfo.InvariantCulture)).ToList(), monthly, monthlyTotals,
            users.OrderBy(u => u.FullName, StringComparer.Ordinal).Select(u => new AiSpendingUserOption(u.UserId, u.FullName)).ToList(),
            rows.Sum(r => r.InputTokens), rows.Sum(r => r.OutputTokens));
    }

    /// <summary>One question, or one AI call, placed on its day in the organization's time zone.</summary>
    private sealed record SpendItem(Guid UserId, DateOnly Day, int Requests, decimal Cost, long InputTokens, long OutputTokens);

    private static int Months(DateOnly first, DateOnly last) => ((last.Year - first.Year) * 12) + last.Month - first.Month + 1;

    private static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
