using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Plans;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Monitoring;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Plans;

/// <summary>
/// Service plans with a monthly AI spending limit (P13.46). The month's spending is the sum of the estimated cost of
/// the organization's analyses since the first day of the month in its own time zone, the same figure the
/// "AI budget" system rule reads. Plans are platform-level; reading an organization's spending here is a dedicated
/// administrator path that returns amounts only, never a question or a row.
/// </summary>
public sealed partial class ServicePlanService : IServicePlanService, IAiSpendingGuard
{
    private readonly ConfigurationDbContext _context;
    private readonly IAuditWriter _audit;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public ServicePlanService(ConfigurationDbContext context, IAuditWriter audit, ITenantContext tenantContext, IClock clock)
    {
        _context = context;
        _audit = audit;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<IReadOnlyList<ServicePlanDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        var counts = await _context.Organizations.AsNoTracking().Where(o => o.ServicePlanId != null)
            .GroupBy(o => o.ServicePlanId!.Value).Select(g => new { PlanId = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.PlanId, g => g.Count, cancellationToken);
        var plans = await _context.ServicePlans.AsNoTracking().OrderBy(p => p.SortOrder).ThenBy(p => p.PlanCode).ToListAsync(cancellationToken);
        return plans.Select(p => ToDto(p, counts.GetValueOrDefault(p.PlanId))).ToList();
    }

    public async Task<ServicePlanDto> CreateAsync(SaveServicePlanCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequirePlatformAdministrator();
        var code = Validate(command);
        if (await _context.ServicePlans.AnyAsync(p => p.PlanCode == code, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.CodeAlreadyExists, "plan_code_exists");
        }

        var plan = new ServicePlan { PlanId = Ids.New(), PlanCode = code };
        Apply(plan, command);
        _context.ServicePlans.Add(plan);
        _audit.Append(AuditEvents.ServicePlanSaved, "ServicePlan", plan.PlanId.ToString(), new { code, limit = plan.MonthlyAiSpendLimit, created = true });
        await _context.SaveChangesAsync(cancellationToken);
        return ToDto(plan, 0);
    }

    public async Task<ServicePlanDto> UpdateAsync(Guid planId, SaveServicePlanCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequirePlatformAdministrator();
        var code = Validate(command);
        var plan = await _context.ServicePlans.SingleOrDefaultAsync(p => p.PlanId == planId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "plan_not_found");
        if (!string.Equals(code, plan.PlanCode, StringComparison.Ordinal))
        {
            // The code is the plan's stable identity (audit entries and exports refer to it).
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "plan_code_immutable");
        }

        var before = plan.MonthlyAiSpendLimit;
        Apply(plan, command);
        _audit.Append(AuditEvents.ServicePlanSaved, "ServicePlan", planId.ToString(), new { code, limit = plan.MonthlyAiSpendLimit, previousLimit = before, active = plan.IsActive });
        await _context.SaveChangesAsync(cancellationToken);
        return ToDto(plan, await _context.Organizations.CountAsync(o => o.ServicePlanId == planId, cancellationToken));
    }

    public async Task<OrganizationPlanDto> GetOrganizationPlanAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        var organization = await _context.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.OrganizationId == organizationId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "organization_not_found");
        return await DescribeAsync(organization, cancellationToken);
    }

    public async Task<OrganizationPlanDto> AssignAsync(Guid organizationId, Guid? planId, CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        var organization = await _context.Organizations.SingleOrDefaultAsync(o => o.OrganizationId == organizationId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "organization_not_found");
        ServicePlan? plan = null;
        if (planId is { } id)
        {
            plan = await _context.ServicePlans.AsNoTracking().SingleOrDefaultAsync(p => p.PlanId == id, cancellationToken)
                ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "plan_not_found");
            if (!plan.IsActive && organization.ServicePlanId != id)
            {
                throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "plan_inactive");
            }
        }

        if (organization.ServicePlanId != planId)
        {
            var previous = organization.ServicePlanId;
            organization.ServicePlanId = planId;
            organization.UpdatedAt = _clock.UtcNow;
            organization.UpdatedByAdministratorId = _tenantContext.Principal.AdministratorId;
            _audit.Append(AuditEvents.OrganizationPlanChanged, "Organization", organizationId.ToString(), new { plan = plan?.PlanCode, limit = plan?.MonthlyAiSpendLimit, previousPlanId = previous });
            await _context.SaveChangesAsync(cancellationToken);
        }

        return await DescribeAsync(organization, cancellationToken);
    }

    public async Task<AiSpendingDto> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        var organization = await _context.Organizations.AsNoTracking().SingleAsync(o => o.OrganizationId == organizationId, cancellationToken);
        var described = await DescribeAsync(organization, cancellationToken);
        if (!_tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin))
        {
            return new AiSpendingDto(described.State, null, null, null, null, null, null);
        }

        decimal? percent = described.MonthlyAiSpendLimit is { } limit && limit > 0 ? Math.Round(described.MonthSpend / limit * 100m, 1) : null;
        return new AiSpendingDto(described.State, described.PlanCode, described.NameEn, described.NameAr, described.MonthlyAiSpendLimit, Math.Round(described.MonthSpend, 2), percent);
    }

    public async Task<bool> IsReachedAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await _context.Organizations.AsNoTracking().IgnoreQueryFilters().SingleOrDefaultAsync(o => o.OrganizationId == organizationId, cancellationToken);
        if (organization?.ServicePlanId is null)
        {
            return false;
        }

        return (await DescribeAsync(organization, cancellationToken)).State == AiSpendingState.Reached;
    }

    private async Task<OrganizationPlanDto> DescribeAsync(Organization organization, CancellationToken cancellationToken)
    {
        var plan = organization.ServicePlanId is { } id ? await _context.ServicePlans.AsNoTracking().SingleOrDefaultAsync(p => p.PlanId == id, cancellationToken) : null;
        // The organization is named explicitly: this also runs for a Platform Administrator, who has no tenant filter.
        var spend = await AiSpend.MonthToDateAsync(_context.AnalysisExecutionMetrics.IgnoreQueryFilters(), organization, _clock.UtcNow, cancellationToken);
        return new OrganizationPlanDto(organization.OrganizationId, plan?.PlanId, plan?.PlanCode, plan?.NameEn, plan?.NameAr, plan?.MonthlyAiSpendLimit, Math.Round(spend, 2),
            AiSpendingState.Of(plan?.MonthlyAiSpendLimit, spend));
    }

    private void Apply(ServicePlan plan, SaveServicePlanCommand command)
    {
        plan.NameEn = command.NameEn.Trim();
        plan.NameAr = command.NameAr.Trim();
        plan.MonthlyAiSpendLimit = command.MonthlyAiSpendLimit is { } limit ? Math.Round(limit, 2) : null;
        plan.IsActive = command.IsActive;
        plan.SortOrder = command.SortOrder;
        plan.UpdatedAt = _clock.UtcNow;
        plan.UpdatedByAdministratorId = _tenantContext.Principal.AdministratorId;
    }

    private static string Validate(SaveServicePlanCommand command)
    {
        var code = (command.PlanCode ?? string.Empty).Trim().ToUpperInvariant();
        if (!CodePattern().IsMatch(code))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "plan_code_invalid");
        }

        if (string.IsNullOrWhiteSpace(command.NameEn) || command.NameEn.Length > 100 || string.IsNullOrWhiteSpace(command.NameAr) || command.NameAr.Length > 100)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "plan_name_required");
        }

        if (command.MonthlyAiSpendLimit is { } limit && limit is <= 0 or > 1_000_000)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "plan_limit_invalid");
        }

        if (command.SortOrder is < 0 or > 1000)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "plan_sort_invalid");
        }

        return code;
    }

    private void RequirePlatformAdministrator()
    {
        if (!_tenantContext.IsPlatformAdministrator)
        {
            throw new TenantContextException("Only platform administrators can manage service plans.");
        }
    }

    private static ServicePlanDto ToDto(ServicePlan p, int organizations)
        => new(p.PlanId, p.PlanCode, p.NameEn, p.NameAr, p.MonthlyAiSpendLimit, p.IsActive, p.SortOrder, organizations, p.UpdatedAt);

    [GeneratedRegex("^[A-Z][A-Z0-9-]{1,31}$")]
    private static partial Regex CodePattern();
}
