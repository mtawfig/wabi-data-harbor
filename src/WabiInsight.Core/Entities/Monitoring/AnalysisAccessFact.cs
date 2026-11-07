using WabiInsight.Core.Entities.Analysis;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Monitoring;

/// <summary>How the analysis that produced the fact ended.</summary>
public enum AccessOutcome
{
    /// <summary>The plan was authorized and executed.</summary>
    Succeeded = 1,

    /// <summary>The plan was rejected before execution for a reason other than permissions (shape, compile).</summary>
    Rejected = 2,

    /// <summary>The plan referenced an object or column the user is not permitted to use. Recorded because repeated attempts are a signal.</summary>
    Unauthorized = 3,

    /// <summary>The plan was authorized but the source failed to execute it.</summary>
    Failed = 4,
}

/// <summary>
/// One row per object an analysis touched (Phase 14 ledger, specification §35): who, which table, which columns,
/// which filters, how many rows, exports and outcome. Written in the same unit of work as the analysis and
/// never updated or deleted except by the retention job. Access-behaviour rules read this instead of parsing plans.
/// </summary>
public class AnalysisAccessFact : ITenantOwned
{
    public Guid FactId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid UserId { get; set; }

    public Guid? DepartmentId { get; set; }

    /// <summary>Role codes of the user at the time, comma-separated, so baselines per role survive later role changes.</summary>
    public string RoleCodes { get; set; } = string.Empty;

    public Guid ApplicationId { get; set; }

    public Guid? HistoryVersionId { get; set; }

    public Guid? MetricId { get; set; }

    /// <summary>Null when the referenced object is not in the catalogue (typical for unauthorized attempts on unknown names).</summary>
    public Guid? ObjectId { get; set; }

    public string SchemaName { get; set; } = string.Empty;

    public string ObjectName { get; set; } = string.Empty;

    /// <summary>True when the object was the source of at least one query; joined objects carry the row count of the query too but are not primary.</summary>
    public bool IsPrimaryObject { get; set; }

    public DateTimeOffset AccessedAt { get; set; }

    /// <summary>Rows returned by the queries this object was the source of (0 for joined-only objects and for non-executed plans).</summary>
    public int RowCount { get; set; }

    public DataClassification HighestClassification { get; set; } = DataClassification.Internal;

    /// <summary>JSON array of the column ids referenced on this object (select, join, filter).</summary>
    public string ColumnIdsJson { get; set; } = "[]";

    public int ColumnCount { get; set; }

    /// <summary>JSON array of the column ids used in filter conditions on this object.</summary>
    public string FilterColumnIdsJson { get; set; } = "[]";

    /// <summary>Number of literal values in filter conditions on this object (an IN list of 20 account numbers counts 20).</summary>
    public int FilterValueCount { get; set; }

    /// <summary>Literal values in filters on identifier columns (primary keys or columns with an identifier semantic role): "specific customers' accounts".</summary>
    public int IdentifierFilterValueCount { get; set; }

    public bool IsExport { get; set; }

    public ExportFormat? ExportFormat { get; set; }

    public AccessOutcome Outcome { get; set; } = AccessOutcome.Succeeded;

    public string? FailureCode { get; set; }
}
