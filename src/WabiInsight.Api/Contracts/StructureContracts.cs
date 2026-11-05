using System.ComponentModel.DataAnnotations;

namespace WabiInsight.Api.Contracts;

public sealed record SaveDepartmentRequest(
    [Required, MaxLength(32)] string DepartmentCode,
    [Required, MaxLength(200)] string DepartmentNameEn,
    [Required, MaxLength(200)] string DepartmentNameAr,
    bool IsActive = true);

public sealed record SaveSectionRequest(
    [Required] Guid DepartmentId,
    [Required, MaxLength(32)] string SectionCode,
    [Required, MaxLength(200)] string SectionNameEn,
    [Required, MaxLength(200)] string SectionNameAr,
    bool IsActive = true);

public sealed record SaveRoleRequest(
    [Required, MaxLength(64)] string RoleCode,
    [Required, MaxLength(200)] string NameEn,
    [Required, MaxLength(200)] string NameAr,
    [MaxLength(500)] string? Description,
    bool IsActive = true);

public sealed record SaveRoleTemplateRequest(
    [Required, MaxLength(64)] string RoleCode,
    [Required, MaxLength(200)] string NameEn,
    [Required, MaxLength(200)] string NameAr,
    [MaxLength(500)] string? Description,
    bool IsActive = true,
    [Range(0, 1000)] int SortOrder = 100);

public sealed record CreateOrganizationUserRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MaxLength(200)] string FullName,
    [MaxLength(50)] string? EmployeeNumber,
    [MaxLength(50)] string? PhoneNumber,
    Guid? DepartmentId,
    Guid? SectionId,
    [RegularExpression("^(en|ar)$")] string? PreferredLanguage,
    IReadOnlyCollection<string>? RoleCodes);

public sealed record UpdateOrganizationUserRequest(
    [Required, MaxLength(200)] string FullName,
    [MaxLength(50)] string? EmployeeNumber,
    [MaxLength(50)] string? PhoneNumber,
    Guid? DepartmentId,
    Guid? SectionId,
    [Required, RegularExpression("^(en|ar)$")] string PreferredLanguage,
    IReadOnlyCollection<string>? RoleCodes);

public sealed record SetUserStatusRequest([Required, RegularExpression("^(Active|Inactive)$")] string Status);

/// <summary>Create/reset responses for organization users: the temporary password is emailed; DeliveredTo is the masked address.</summary>
public sealed record UserProvisioningResponse(Guid UserId, string DeliveredTo, bool MustChangePassword);
