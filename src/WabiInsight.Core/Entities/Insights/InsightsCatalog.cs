namespace WabiInsight.Core.Entities.Insights;

/// <summary>
/// Global catalog of the built-in Platform Insights application (specification §12). Rows carry no
/// OrganizationId: the catalog is shared by every organization and maintained by Platform Administrators.
/// Data is read only through the tenant-scoped <c>insights</c> views, never from these rows.
/// </summary>
public class InsightsObject
{
    public Guid InsightsObjectId { get; set; }

    /// <summary>View name inside the <c>insights</c> schema (identifier characters only).</summary>
    public string ViewName { get; set; } = string.Empty;

    public string DisplayNameEn { get; set; } = string.Empty;

    public string DisplayNameAr { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Only approved views (and approved columns) are exposed to organizations and to the AI engine.</summary>
    public bool IsApproved { get; set; }

    /// <summary>False when the view disappeared from the database at the last sync.</summary>
    public bool IsPresent { get; set; } = true;

    public int SortOrder { get; set; } = 100;

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedByAdministratorId { get; set; }
}

public class InsightsColumn
{
    public Guid InsightsColumnId { get; set; }

    public Guid InsightsObjectId { get; set; }

    public string ColumnName { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    public bool IsNullable { get; set; }

    public int OrdinalPosition { get; set; }

    public string DisplayNameEn { get; set; } = string.Empty;

    public string DisplayNameAr { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsApproved { get; set; }

    public bool IsPresent { get; set; } = true;
}

public enum MetricAggregate
{
    Count = 1,
    CountDistinct = 2,
    Sum = 3,
    Average = 4,
    Min = 5,
    Max = 6,
}

/// <summary>
/// A named, approved measure over one insights view: an aggregate of a column (or row count) with fixed
/// filters, optionally bucketed over a date column. Definitions are data; the SQL is built by
/// <see cref="Insights.InsightsSqlBuilder"/> and always carries the organization predicate.
/// </summary>
public class InsightsMetric
{
    public Guid InsightsMetricId { get; set; }

    public string MetricCode { get; set; } = string.Empty;

    public Guid InsightsObjectId { get; set; }

    public string DisplayNameEn { get; set; } = string.Empty;

    public string DisplayNameAr { get; set; } = string.Empty;

    public string? Description { get; set; }

    public MetricAggregate Aggregate { get; set; } = MetricAggregate.Count;

    /// <summary>Column aggregated; null for Count.</summary>
    public string? ColumnName { get; set; }

    /// <summary>Column used for period filters and time buckets; null for point-in-time metrics.</summary>
    public string? DateColumn { get; set; }

    /// <summary>JSON array of fixed filters: [{"column":"EventType","operator":"eq","values":["Auth.Login"]}].</summary>
    public string FiltersJson { get; set; } = "[]";

    /// <summary>Display unit code (count, minutes, tokens, ...); labels are localized in the UI.</summary>
    public string Unit { get; set; } = "count";

    public bool IsApproved { get; set; }

    public int SortOrder { get; set; } = 100;

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedByAdministratorId { get; set; }
}
