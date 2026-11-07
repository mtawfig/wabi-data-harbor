using WabiInsight.Core.Common;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Metadata;

public enum DatabaseObjectType
{
    Table = 1,
    View = 2,
}

/// <summary>Data classification (review B11): drives external-disclosure rules in later phases.</summary>
public enum DataClassification
{
    Internal = 1,
    Confidential = 2,
    Restricted = 3,
}

/// <summary>
/// Optional meaning of a column, set by the Organization Administrator (Phase 14, specification §35.1). A hint that
/// lets rule templates and the natural-language planner bind to the right fields in any business; never a permission.
/// Stable codes; labels are localized in the web app.
/// </summary>
public enum ColumnSemanticRole
{
    /// <summary>Clears the role (stored as NULL).</summary>
    Unspecified = 0,
    Identifier = 1,
    PersonIdentifier = 2,
    AccountIdentifier = 3,
    DeviceIdentifier = 4,
    Timestamp = 5,
    Amount = 6,
    Quantity = 7,
    Status = 8,
    Location = 9,
    Contact = 10,
    Other = 11,
}

/// <summary>A discovered table or view of an organization's application (specification §17). Only approved objects are ever exposed to AI or queries.</summary>
public class DatabaseObject : AuditableEntity, ITenantOwned
{
    public Guid ObjectId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ApplicationId { get; set; }

    public string SchemaName { get; set; } = string.Empty;

    public string ObjectName { get; set; } = string.Empty;

    public DatabaseObjectType ObjectType { get; set; }

    public string? DisplayNameEn { get; set; }

    public string? DisplayNameAr { get; set; }

    /// <summary>User content: length-limited and treated as untrusted when passed to the LLM.</summary>
    public string? BusinessDescription { get; set; }

    public bool IsApproved { get; set; }

    /// <summary>False once a refresh no longer finds the object in the source; the row is kept so permissions and history stay intact.</summary>
    public bool IsPresent { get; set; } = true;

    public DateTimeOffset DiscoveredAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset? RemovedAt { get; set; }

    public Guid? ApprovedByUserId { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }
}

public class DatabaseColumn : AuditableEntity, ITenantOwned
{
    public Guid ColumnId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ObjectId { get; set; }

    public string ColumnName { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    public int? MaxLength { get; set; }

    public int? Precision { get; set; }

    public int? Scale { get; set; }

    public bool IsNullable { get; set; }

    public bool IsPrimaryKey { get; set; }

    public int OrdinalPosition { get; set; }

    public string? DisplayNameEn { get; set; }

    public string? DisplayNameAr { get; set; }

    public string? BusinessDescription { get; set; }

    public DataClassification Classification { get; set; } = DataClassification.Internal;

    /// <summary>Null until the Organization Administrator chooses one; <see cref="ColumnSemanticRole.Unspecified"/> is never stored.</summary>
    public ColumnSemanticRole? SemanticRole { get; set; }

    public bool IsApproved { get; set; }

    public bool IsPresent { get; set; } = true;

    public DateTimeOffset DiscoveredAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset? RemovedAt { get; set; }

    /// <summary>Identifier-like for the ledger: primary keys and identifier roles count "specific records" in filters.</summary>
    public bool IsIdentifierLike => IsPrimaryKey || SemanticRole is ColumnSemanticRole.Identifier or ColumnSemanticRole.PersonIdentifier or ColumnSemanticRole.AccountIdentifier or ColumnSemanticRole.DeviceIdentifier;
}

/// <summary>Foreign-key relationship between two discovered objects of the same application.</summary>
public class DatabaseRelationship : ITenantOwned
{
    public Guid RelationshipId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ApplicationId { get; set; }

    public string ConstraintName { get; set; } = string.Empty;

    public Guid FromObjectId { get; set; }

    public Guid FromColumnId { get; set; }

    public Guid ToObjectId { get; set; }

    public Guid ToColumnId { get; set; }

    public bool IsPresent { get; set; } = true;

    public DateTimeOffset DiscoveredAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}
