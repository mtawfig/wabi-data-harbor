namespace WabiInsight.Core.Organizations;

public sealed record DepartmentDto(Guid DepartmentId, string DepartmentCode, string DepartmentNameEn, string DepartmentNameAr, bool IsActive, int SectionCount, int UserCount);

public sealed record SectionDto(Guid SectionId, Guid DepartmentId, string SectionCode, string SectionNameEn, string SectionNameAr, bool IsActive, int UserCount);

public sealed record SaveDepartmentCommand(string DepartmentCode, string DepartmentNameEn, string DepartmentNameAr, bool IsActive);

public sealed record SaveSectionCommand(Guid DepartmentId, string SectionCode, string SectionNameEn, string SectionNameAr, bool IsActive);

/// <summary>Departments and sections of the signed-in user's organization. Requires the OrganizationAdmin role for writes.</summary>
public interface IOrganizationStructureService
{
    Task<IReadOnlyList<DepartmentDto>> ListDepartmentsAsync(bool includeInactive = true, CancellationToken cancellationToken = default);

    Task<DepartmentDto> CreateDepartmentAsync(SaveDepartmentCommand command, CancellationToken cancellationToken = default);

    Task<DepartmentDto> UpdateDepartmentAsync(Guid departmentId, SaveDepartmentCommand command, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SectionDto>> ListSectionsAsync(Guid? departmentId = null, bool includeInactive = true, CancellationToken cancellationToken = default);

    Task<SectionDto> CreateSectionAsync(SaveSectionCommand command, CancellationToken cancellationToken = default);

    Task<SectionDto> UpdateSectionAsync(Guid sectionId, SaveSectionCommand command, CancellationToken cancellationToken = default);
}

/// <summary>Raised by organization services; the API maps Outcome to a problem response.</summary>
public sealed class DomainException : Exception
{
    public DomainException()
    {
    }

    public DomainException(string message)
        : base(message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public DomainException(OrganizationOperationOutcome outcome, params string[] errors)
        : base(string.Join(", ", errors))
    {
        Outcome = outcome;
        Errors = errors;
    }

    public OrganizationOperationOutcome Outcome { get; } = OrganizationOperationOutcome.ValidationFailed;

    public IReadOnlyList<string> Errors { get; } = [];
}
