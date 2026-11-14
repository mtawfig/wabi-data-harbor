using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Platform;

/// <summary>
/// Remembers that an organization's administrators were told about a spending threshold in a month (P13.47), so
/// the alert goes out once per threshold per month and not on every request. A plan change removes the month's rows.
/// </summary>
public class AiSpendingNotice : ITenantOwned
{
    public Guid NoticeId { get; set; }

    public Guid OrganizationId { get; set; }

    /// <summary>The month in the organization's time zone, as yyyy-MM.</summary>
    public string Month { get; set; } = string.Empty;

    /// <summary><c>near</c> (80% of the limit) or <c>reached</c>.</summary>
    public string Threshold { get; set; } = string.Empty;

    public decimal MonthlyAiSpendLimit { get; set; }

    public decimal MonthSpend { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
