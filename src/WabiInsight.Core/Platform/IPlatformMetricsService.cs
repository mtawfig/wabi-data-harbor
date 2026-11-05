using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Platform;

public enum MetricBucket
{
    Day = 1,
    Week = 2,
    Month = 3,
}

/// <summary>Inclusive UTC range used by every dashboard query; defaults to the last 30 days.</summary>
public sealed record MetricPeriod(DateTimeOffset From, DateTimeOffset To)
{
    public static MetricPeriod LastDays(DateTimeOffset now, int days) => new(now.AddDays(-days).Date, now);
}

/// <summary>
/// Platform KPI cards (specification §13.1). Counts that later phases produce (AI, reports, support) are
/// reported with <c>Collected = false</c> until their phase ships, so the dashboard never shows a fake zero.
/// </summary>
public sealed record MetricValue(string Code, long Value, bool Collected = true);

public sealed record PlatformDashboard(
    MetricPeriod Period,
    IReadOnlyList<MetricValue> Kpis,
    IReadOnlyList<MetricSeries> Series,
    IReadOnlyList<OrganizationUsageRow> OrganizationUsage,
    IReadOnlyList<PlatformErrorRow> RecentErrors);

public sealed record MetricPoint(DateTimeOffset Bucket, long Value);

public sealed record MetricSeries(string Code, IReadOnlyList<MetricPoint> Points);

/// <summary>Aggregate per organization for the operational table: identity and counts only.</summary>
public sealed record OrganizationUsageRow(Guid OrganizationId, string OrganizationCode, string OrganizationName, string Status, long Users, long ActiveUsersInPeriod, long Logins, long EmailsSent, long EmailsFailed);

public sealed record PlatformErrorRow(DateTimeOffset At, string EventType, Guid? OrganizationId, string? Detail);

public interface IPlatformMetricsService
{
    /// <summary>Platform administrators only. Aggregates across organizations without exposing application names or business data.</summary>
    Task<PlatformDashboard> GetDashboardAsync(MetricPeriod period, MetricBucket bucket, Guid? organizationId = null, CancellationToken cancellationToken = default);
}

/// <summary>One audit row as shown to platform administrators (secrets are never written to audit details).</summary>
public sealed record AuditLogRow(Guid AuditLogId, DateTimeOffset CreatedAt, Guid? OrganizationId, PrincipalType ActorType, Guid? ActorId, string ActorDisplayName, string EventType, string? EntityType, string? EntityId, string? DetailsJson, string? CorrelationId, string? IpAddress);

public sealed record AuditLogQuery(int Page = 1, int PageSize = 50, DateTimeOffset? From = null, DateTimeOffset? To = null, string? EventType = null, Guid? OrganizationId = null, PrincipalType? ActorType = null, string? Search = null);

public interface IAuditQueryService
{
    Task<Organizations.PagedResult<AuditLogRow>> ListAsync(AuditLogQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListEventTypesAsync(CancellationToken cancellationToken = default);
}

public sealed record HealthCheckEntry(string Name, string Status, string? Description, double DurationMs, IReadOnlyDictionary<string, object> Data);

public sealed record PlatformHealthReport(string Status, DateTimeOffset CheckedAt, double TotalDurationMs, IReadOnlyList<HealthCheckEntry> Checks);

public interface IPlatformHealthService
{
    Task<PlatformHealthReport> CheckAsync(CancellationToken cancellationToken = default);
}
