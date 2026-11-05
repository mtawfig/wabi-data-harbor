using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Organizations;

namespace WabiInsight.Core.Metadata;

public sealed record ObjectSummaryDto(Guid ObjectId, Guid ApplicationId, string SchemaName, string ObjectName, DatabaseObjectType ObjectType, string? DisplayNameEn, string? DisplayNameAr, bool IsApproved, bool IsPresent, int ColumnCount, int ApprovedColumnCount, DateTimeOffset DiscoveredAt, DateTimeOffset LastSeenAt);

public sealed record ColumnDto(Guid ColumnId, string ColumnName, string DataType, int? MaxLength, int? Precision, int? Scale, bool IsNullable, bool IsPrimaryKey, int OrdinalPosition, string? DisplayNameEn, string? DisplayNameAr, string? BusinessDescription, DataClassification Classification, bool IsApproved, bool IsPresent);

public sealed record RelationshipDto(Guid RelationshipId, string ConstraintName, Guid FromObjectId, string FromObject, string FromColumn, Guid ToObjectId, string ToObject, string ToColumn, bool IsPresent);

public sealed record ObjectDetailDto(ObjectSummaryDto Summary, string? BusinessDescription, IReadOnlyList<ColumnDto> Columns, IReadOnlyList<RelationshipDto> Relationships);

public sealed record ObjectListQuery(Guid ApplicationId, int Page = 1, int PageSize = 50, string? Search = null, bool? Approved = null, bool IncludeRemoved = false);

public sealed record UpdateObjectCommand(string? DisplayNameEn, string? DisplayNameAr, string? BusinessDescription, bool IsApproved);

public sealed record UpdateColumnCommand(string? DisplayNameEn, string? DisplayNameAr, string? BusinessDescription, DataClassification Classification, bool IsApproved);

/// <summary>What a refresh changed. New objects and columns stay unapproved; removed ones are flagged, never deleted.</summary>
public sealed record MetadataRefreshResult(int ObjectsAdded, int ObjectsUpdated, int ObjectsRemoved, int ColumnsAdded, int ColumnsRemoved, int RelationshipsAdded, int RelationshipsRemoved, DateTimeOffset RefreshedAt);

/// <summary>Metadata catalog of an organization's applications (specification §17). Organization Administrators only; support grants reach it through the executor.</summary>
public interface IMetadataService
{
    /// <summary>Discovers the source schema and merges it into the catalog with a diff.</summary>
    Task<MetadataRefreshResult> RefreshAsync(Guid applicationId, CancellationToken cancellationToken = default);

    Task<PagedResult<ObjectSummaryDto>> ListObjectsAsync(ObjectListQuery query, CancellationToken cancellationToken = default);

    Task<ObjectDetailDto?> GetObjectAsync(Guid objectId, CancellationToken cancellationToken = default);

    Task<ObjectDetailDto> UpdateObjectAsync(Guid objectId, UpdateObjectCommand command, CancellationToken cancellationToken = default);

    Task<ColumnDto> UpdateColumnAsync(Guid columnId, UpdateColumnCommand command, CancellationToken cancellationToken = default);

    /// <summary>Approves an object and all its present columns in one step.</summary>
    Task<ObjectDetailDto> ApproveObjectWithColumnsAsync(Guid objectId, bool approve, CancellationToken cancellationToken = default);
}
