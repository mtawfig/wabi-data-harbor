using WabiInsight.Core.Entities.Permissions;

namespace WabiInsight.Api.Contracts;

public sealed record SetObjectGrantRequest(bool Granted, bool AllColumns = true, IReadOnlyList<Guid>? ColumnIds = null, RowScope? RowScope = null);

public sealed record SetRowMappingRequest(Guid? EmployeeColumnId, Guid? DepartmentColumnId, Guid? SectionColumnId);
