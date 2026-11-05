using WabiInsight.Core.Entities.Organizations;

namespace WabiInsight.Core.Organizations;

public sealed record OrganizationUserDto(
    Guid UserId,
    string Email,
    string FullName,
    string? EmployeeNumber,
    string? PhoneNumber,
    Guid? DepartmentId,
    Guid? SectionId,
    string PreferredLanguage,
    OrganizationUserStatus Status,
    bool MustChangePassword,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Roles);

public sealed record OrganizationUserListQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    OrganizationUserStatus? Status = null,
    Guid? DepartmentId = null,
    string? RoleCode = null);

public sealed record CreateOrganizationUserCommand(
    string Email,
    string FullName,
    string? EmployeeNumber,
    string? PhoneNumber,
    Guid? DepartmentId,
    Guid? SectionId,
    string PreferredLanguage,
    IReadOnlyCollection<string> RoleCodes);

public sealed record UpdateOrganizationUserCommand(
    string FullName,
    string? EmployeeNumber,
    string? PhoneNumber,
    Guid? DepartmentId,
    Guid? SectionId,
    string PreferredLanguage,
    IReadOnlyCollection<string> RoleCodes);

/// <summary>
/// Organization user management for Organization Administrators (specification §9). Every operation is
/// scoped to the administrator's own organization; temporary passwords are emailed, never returned.
/// </summary>
public interface IOrganizationUserService
{
    Task<PagedResult<OrganizationUserDto>> ListAsync(OrganizationUserListQuery query, CancellationToken cancellationToken = default);

    Task<OrganizationUserDto?> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<(OrganizationUserDto User, string DeliveredTo)> CreateAsync(CreateOrganizationUserCommand command, CancellationToken cancellationToken = default);

    Task<OrganizationUserDto> UpdateAsync(Guid userId, UpdateOrganizationUserCommand command, CancellationToken cancellationToken = default);

    Task<OrganizationUserDto> SetStatusAsync(Guid userId, OrganizationUserStatus status, CancellationToken cancellationToken = default);

    /// <summary>Issues a new temporary password by email; the user must change it at next sign-in.</summary>
    Task<string> ResetPasswordAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed record RoleDto(Guid RoleId, string RoleCode, string NameEn, string NameAr, string? Description, bool IsSystem, bool IsActive, int? TemplateVersion, int MemberCount);

public sealed record SaveRoleCommand(string RoleCode, string NameEn, string NameAr, string? Description, bool IsActive);

/// <summary>Roles of the signed-in user's organization. System roles keep their code and cannot be deleted.</summary>
public interface IRoleService
{
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<RoleDto> CreateAsync(SaveRoleCommand command, CancellationToken cancellationToken = default);

    Task<RoleDto> UpdateAsync(Guid roleId, SaveRoleCommand command, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid roleId, CancellationToken cancellationToken = default);
}

public sealed record RoleTemplateDto(Guid TemplateId, string RoleCode, string NameEn, string NameAr, string? Description, bool IsSystem, bool IsActive, int SortOrder, int Version, DateTimeOffset UpdatedAt);

public sealed record SaveRoleTemplateCommand(string RoleCode, string NameEn, string NameAr, string? Description, bool IsActive, int SortOrder);

/// <summary>Platform-managed default role templates (Platform Administrators). Used only when a new organization is created.</summary>
public interface IRoleTemplateService
{
    Task<IReadOnlyList<RoleTemplateDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<RoleTemplateDto> CreateAsync(SaveRoleTemplateCommand command, CancellationToken cancellationToken = default);

    Task<RoleTemplateDto> UpdateAsync(Guid templateId, SaveRoleTemplateCommand command, CancellationToken cancellationToken = default);

    /// <summary>Active templates in provisioning order; falls back to the built-in catalog when the table is empty.</summary>
    Task<IReadOnlyList<RoleTemplateDto>> GetProvisioningTemplatesAsync(CancellationToken cancellationToken = default);
}
