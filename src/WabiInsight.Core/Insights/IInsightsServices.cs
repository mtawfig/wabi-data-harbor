using WabiInsight.Core.Applications;
using WabiInsight.Core.Entities.Insights;

namespace WabiInsight.Core.Insights;

public sealed record InsightsColumnDto(Guid ColumnId, string ColumnName, string DataType, bool IsNullable, int OrdinalPosition, string DisplayNameEn, string DisplayNameAr, string? Description, bool IsApproved, bool IsPresent);

public sealed record InsightsObjectDto(Guid ObjectId, string ViewName, string DisplayNameEn, string DisplayNameAr, string? Description, bool IsApproved, bool IsPresent, int SortOrder, DateTimeOffset UpdatedAt, IReadOnlyList<InsightsColumnDto> Columns);

public sealed record InsightsMetricDto(Guid MetricId, string MetricCode, Guid ObjectId, string ViewName, string DisplayNameEn, string DisplayNameAr, string? Description, MetricAggregate Aggregate, string? ColumnName, string? DateColumn, IReadOnlyList<MetricFilter> Filters, string Unit, bool IsApproved, int SortOrder, DateTimeOffset UpdatedAt);

/// <summary>State of the dedicated insights login (specification §12.1): configured, reachable, confined to the insights schema.</summary>
public sealed record InsightsSourceStatus(bool DedicatedLoginConfigured, bool Reachable, bool? ConfinedToInsightsSchema, int ViewCount, IReadOnlyList<string> Warnings, string? Error, DateTimeOffset CheckedAt);

public sealed record InsightsCatalogDto(InsightsSourceStatus Source, IReadOnlyList<InsightsObjectDto> Objects, IReadOnlyList<InsightsMetricDto> Metrics);

public sealed record InsightsSyncResult(int ObjectsAdded, int ObjectsRemoved, int ColumnsAdded, int ColumnsRemoved, int ColumnsChanged);

public sealed record UpdateInsightsObjectCommand(string DisplayNameEn, string DisplayNameAr, string? Description, bool IsApproved, int SortOrder);

public sealed record UpdateInsightsColumnCommand(string DisplayNameEn, string DisplayNameAr, string? Description, bool IsApproved);

public sealed record SaveInsightsMetricCommand(string MetricCode, Guid ObjectId, string DisplayNameEn, string DisplayNameAr, string? Description, MetricAggregate Aggregate, string? ColumnName, string? DateColumn, IReadOnlyList<MetricFilter> Filters, string Unit, bool IsApproved, int SortOrder);

/// <summary>Platform Administrators maintain the global catalog; organizations only read the approved part of it.</summary>
public interface IInsightsCatalogService
{
    Task<InsightsCatalogDto> GetCatalogAsync(CancellationToken cancellationToken = default);

    Task<InsightsSourceStatus> CheckSourceAsync(CancellationToken cancellationToken = default);

    /// <summary>Reconciles the catalog with the views that exist in the insights schema; new views/columns stay unapproved, missing ones are flagged.</summary>
    Task<InsightsSyncResult> SyncAsync(CancellationToken cancellationToken = default);

    Task<InsightsObjectDto> UpdateObjectAsync(Guid objectId, UpdateInsightsObjectCommand command, CancellationToken cancellationToken = default);

    Task<InsightsColumnDto> UpdateColumnAsync(Guid columnId, UpdateInsightsColumnCommand command, CancellationToken cancellationToken = default);

    Task<InsightsMetricDto> CreateMetricAsync(SaveInsightsMetricCommand command, CancellationToken cancellationToken = default);

    Task<InsightsMetricDto> UpdateMetricAsync(Guid metricId, SaveInsightsMetricCommand command, CancellationToken cancellationToken = default);

    Task DeleteMetricAsync(Guid metricId, CancellationToken cancellationToken = default);

    /// <summary>SQL that would run for a metric with a placeholder tenant; platform administrators never execute it.</summary>
    Task<BuiltQuery> PreviewSqlAsync(Guid metricId, MetricQueryRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Approved catalog as an organization sees it (no sync state, no unapproved rows).</summary>
public sealed record OrganizationInsightsCatalog(IReadOnlyList<InsightsObjectDto> Objects, IReadOnlyList<InsightsMetricDto> Metrics);

public sealed record MetricRow(DateTimeOffset? Bucket, string? Group, double Value);

public sealed record MetricResult(string MetricCode, string Unit, IReadOnlyList<MetricRow> Rows, double DurationMs);

/// <summary>
/// Executes approved metrics for the signed-in organization through the read-only insights login.
/// The organization always comes from <see cref="Tenancy.ITenantContext"/>; nothing in the request can change it.
/// </summary>
public interface IInsightsQueryService
{
    Task<OrganizationInsightsCatalog> GetApprovedCatalogAsync(CancellationToken cancellationToken = default);

    Task<MetricResult> QueryMetricAsync(string metricCode, MetricQueryRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Opens connections for insights queries: the dedicated read-only login when configured.</summary>
public interface IInsightsConnectionFactory
{
    bool DedicatedLoginConfigured { get; }

    Task<System.Data.Common.DbConnection> OpenAsync(CancellationToken cancellationToken = default);

    Task<ConnectionTestResult> TestAsync(CancellationToken cancellationToken = default);
}
