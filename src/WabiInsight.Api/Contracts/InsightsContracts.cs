using System.ComponentModel.DataAnnotations;
using WabiInsight.Core.Entities.Insights;
using WabiInsight.Core.Insights;
using WabiInsight.Core.Platform;

namespace WabiInsight.Api.Contracts;

public sealed record UpdateInsightsObjectRequest([Required, MaxLength(200)] string DisplayNameEn, [Required, MaxLength(200)] string DisplayNameAr, [MaxLength(1000)] string? Description, bool IsApproved, [Range(0, 10000)] int SortOrder = 100);

public sealed record UpdateInsightsColumnRequest([Required, MaxLength(200)] string DisplayNameEn, [Required, MaxLength(200)] string DisplayNameAr, [MaxLength(1000)] string? Description, bool IsApproved);

public sealed record MetricFilterRequest([Required, MaxLength(128)] string Column, [Required, MaxLength(8)] string Operator, [Required] IReadOnlyList<string> Values);

public sealed record SaveInsightsMetricRequest(
    [Required, MaxLength(100)] string MetricCode,
    [Required] Guid ObjectId,
    [Required, MaxLength(200)] string DisplayNameEn,
    [Required, MaxLength(200)] string DisplayNameAr,
    [MaxLength(1000)] string? Description,
    MetricAggregate Aggregate,
    [MaxLength(128)] string? ColumnName,
    [MaxLength(128)] string? DateColumn,
    IReadOnlyList<MetricFilterRequest>? Filters,
    [MaxLength(32)] string Unit = "count",
    bool IsApproved = false,
    [Range(0, 10000)] int SortOrder = 100)
{
    public SaveInsightsMetricCommand ToCommand() => new(MetricCode, ObjectId, DisplayNameEn, DisplayNameAr, Description, Aggregate, ColumnName, DateColumn, (Filters ?? []).Select(f => new MetricFilter(f.Column, f.Operator, f.Values)).ToList(), Unit, IsApproved, SortOrder);
}

/// <summary>Period, bucket, grouping and extra filters of one metric query. The organization is never part of it.</summary>
public sealed record MetricQueryRequestBody(DateTimeOffset? From, DateTimeOffset? To, MetricBucket? Bucket, [MaxLength(128)] string? GroupBy, IReadOnlyList<MetricFilterRequest>? Filters)
{
    public MetricQueryRequest ToRequest() => new(From, To, Bucket, GroupBy, Filters?.Select(f => new MetricFilter(f.Column, f.Operator, f.Values)).ToList());
}

public sealed record SqlPreviewResponse(string Sql, IReadOnlyDictionary<string, string> Parameters);
