using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Permissions;

namespace WabiInsight.Core.Permissions;

public sealed record ColumnGrantDto(Guid ColumnId, string ColumnName, string DataType, DataClassification Classification, bool Granted);

public sealed record ObjectGrantDto(
    Guid ObjectId,
    Guid ApplicationId,
    string SchemaName,
    string ObjectName,
    DatabaseObjectType ObjectType,
    string? DisplayNameEn,
    string? DisplayNameAr,
    bool IsApproved,
    bool IsPresent,
    bool Granted,
    bool AllColumns,
    RowScope? RowScope,
    IReadOnlyList<ColumnGrantDto> Columns,
    MappingAvailability Mapping);

public sealed record RoleMatrixDto(Guid RoleId, string RoleCode, bool IsSystemManagement, Guid ApplicationId, IReadOnlyList<ObjectGrantDto> Objects);

public sealed record SetObjectGrantCommand(bool Granted, bool AllColumns, IReadOnlyList<Guid> ColumnIds, RowScope? RowScope);

public sealed record RowMappingDto(Guid ObjectId, Guid? EmployeeColumnId, Guid? DepartmentColumnId, Guid? SectionColumnId, IReadOnlyList<ColumnGrantDto> Columns);

public sealed record SetRowMappingCommand(Guid? EmployeeColumnId, Guid? DepartmentColumnId, Guid? SectionColumnId);

/// <summary>Effective access of one principal to one object: what the compiler (Phase 8) is allowed to emit.</summary>
public sealed record EffectiveObjectAccess(
    Guid ApplicationId,
    string ApplicationCode,
    Guid ObjectId,
    string SchemaName,
    string ObjectName,
    DatabaseObjectType ObjectType,
    string? DisplayNameEn,
    string? DisplayNameAr,
    IReadOnlyList<EffectiveColumn> Columns,
    IReadOnlyList<RowScope> RowScopes,
    IReadOnlyList<RowScope> UnmappedScopes,
    bool YieldsRows,
    IReadOnlyList<string> ViaRoles);

public sealed record EffectiveColumn(Guid ColumnId, string ColumnName, string DataType, string? DisplayNameEn, string? DisplayNameAr, DataClassification Classification);

/// <summary>Values compared by row predicates (review B13), taken from the authenticated user, never from the request.</summary>
public sealed record RowScopeContext(string? EmployeeNumber, string? DepartmentCode, string? SectionCode);

public sealed record EffectivePermissions(Guid UserId, Guid OrganizationId, IReadOnlyList<string> Roles, RowScopeContext Context, IReadOnlyList<EffectiveObjectAccess> Objects);

/// <summary>Resolves effective permissions (specification §10): organization boundary first, approved application and objects only, union across active roles.</summary>
public interface IPermissionResolver
{
    Task<EffectivePermissions> ResolveAsync(CancellationToken cancellationToken = default);

    /// <summary>Organization Administrators preview another user of their organization.</summary>
    Task<EffectivePermissions> ResolveForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Explicitly scoped resolution used by the AI engine and by the executor; the organization must match the caller's or a grant.</summary>
    Task<EffectivePermissions> ResolveForUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Organization Administrators configure table, column and row permissions of their roles.</summary>
public interface IPermissionService
{
    Task<RoleMatrixDto> GetRoleMatrixAsync(Guid roleId, Guid applicationId, CancellationToken cancellationToken = default);

    Task<ObjectGrantDto> SetObjectGrantAsync(Guid roleId, Guid objectId, SetObjectGrantCommand command, CancellationToken cancellationToken = default);

    Task<RowMappingDto> GetRowMappingAsync(Guid objectId, CancellationToken cancellationToken = default);

    Task<RowMappingDto> SetRowMappingAsync(Guid objectId, SetRowMappingCommand command, CancellationToken cancellationToken = default);
}
