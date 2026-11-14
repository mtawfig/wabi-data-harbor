namespace WabiInsight.Core.Plans;

public sealed record ServicePlanDto(Guid PlanId, string PlanCode, string NameEn, string NameAr, decimal? MonthlyAiSpendLimit, bool IsActive, int SortOrder, int OrganizationCount, DateTimeOffset UpdatedAt);

public sealed record SaveServicePlanCommand(string PlanCode, string NameEn, string NameAr, decimal? MonthlyAiSpendLimit, bool IsActive, int SortOrder);

/// <summary>An organization's plan and what its AI requests have cost so far this month (platform view).</summary>
public sealed record OrganizationPlanDto(Guid OrganizationId, Guid? PlanId, string? PlanCode, string? NameEn, string? NameAr, decimal? MonthlyAiSpendLimit, decimal MonthSpend, string State);

/// <summary>
/// What an organization user is told about the month's AI spending. Everyone gets the state; the plan, the limit and
/// the amount spent are for Organization Administrators only (usage figures are theirs, P13.28).
/// </summary>
public sealed record AiSpendingDto(string State, string? PlanCode, string? NameEn, string? NameAr, decimal? MonthlyAiSpendLimit, decimal? MonthSpend, decimal? PercentUsed);

/// <summary>States of an organization's AI spending against its plan's monthly limit.</summary>
public static class AiSpendingState
{
    /// <summary>No plan, or a plan without a limit.</summary>
    public const string Unlimited = "unlimited";

    public const string Within = "within";

    /// <summary>At or above <see cref="NearPercent"/> of the limit.</summary>
    public const string Near = "near";

    /// <summary>The limit is reached: new AI requests are refused until the next month or a plan change.</summary>
    public const string Reached = "reached";

    public const decimal NearPercent = 80m;

    public const string ReachedCode = "ai_spending_limit_reached";

    public static string Of(decimal? limit, decimal spend)
        => limit is not { } l ? Unlimited : spend >= l ? Reached : l > 0 && spend / l * 100m >= NearPercent ? Near : Within;
}

/// <summary>Service plans and their monthly AI spending limits (Platform Administrators), P13.46.</summary>
public interface IServicePlanService
{
    Task<IReadOnlyList<ServicePlanDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<ServicePlanDto> CreateAsync(SaveServicePlanCommand command, CancellationToken cancellationToken = default);

    Task<ServicePlanDto> UpdateAsync(Guid planId, SaveServicePlanCommand command, CancellationToken cancellationToken = default);

    Task<OrganizationPlanDto> GetOrganizationPlanAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>Gives the organization a plan, or none (<paramref name="planId"/> null: no limit).</summary>
    Task<OrganizationPlanDto> AssignAsync(Guid organizationId, Guid? planId, CancellationToken cancellationToken = default);
}

/// <summary>Checked before a request that costs model tokens; the organization always comes from the signed-in principal.</summary>
public interface IAiSpendingGuard
{
    /// <summary>The signed-in user's organization: state for everyone, figures for its administrators.</summary>
    Task<AiSpendingDto> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>True when the organization's plan limit for this month is reached.</summary>
    Task<bool> IsReachedAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the organization's administrators, once per threshold per month, that spending is near the limit or has
    /// reached it (in-app notification and e-mail). Called after a request that cost something.
    /// </summary>
    Task AlertAdministratorsAsync(Guid organizationId, CancellationToken cancellationToken = default);
}

/// <summary>What the spending report covers. Dates are days in the organization's time zone, both included.</summary>
public sealed record AiSpendingReportFilter(DateOnly? From, DateOnly? To, Guid? UserId, decimal? MinSpend, decimal? MaxSpend, bool SuccessfulOnly);

public sealed record AiSpendingUserRow(Guid UserId, string FullName, string? EmployeeNumber, string? DepartmentNameEn, string? DepartmentNameAr, bool IsActive, int Requests, decimal Spend, decimal SharePercent);

public sealed record AiSpendingMonthlyRow(Guid UserId, string FullName, IReadOnlyList<decimal> Spend, decimal Total);

public sealed record AiSpendingUserOption(Guid UserId, string FullName);

/// <summary>
/// The Organization Administrator's spending report (P13.47): the plan and the month's position, the period's totals,
/// one row per employee, and the same spending month by month. Amounts and counts only.
/// </summary>
public sealed record AiSpendingReportDto(
    string State, string? PlanCode, string? PlanNameEn, string? PlanNameAr, decimal? MonthlyAiSpendLimit, decimal MonthSpend, decimal? PercentUsed,
    DateOnly From, DateOnly To, int TotalRequests, decimal TotalSpend, IReadOnlyList<AiSpendingUserRow> Rows,
    IReadOnlyList<string> Months, IReadOnlyList<AiSpendingMonthlyRow> Monthly, IReadOnlyList<decimal> MonthlyTotals,
    IReadOnlyList<AiSpendingUserOption> Users);

/// <summary>Per-employee AI spending of the signed-in administrator's own organization.</summary>
public interface IAiSpendingReportService
{
    Task<AiSpendingReportDto> GetAsync(AiSpendingReportFilter filter, CancellationToken cancellationToken = default);
}
