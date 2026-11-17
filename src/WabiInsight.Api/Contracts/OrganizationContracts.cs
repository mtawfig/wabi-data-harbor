using System.ComponentModel.DataAnnotations;

namespace WabiInsight.Api.Contracts;

public sealed record CreateOrganizationRequest(
    [Required, MaxLength(32)] string OrganizationCode,
    [Required, MaxLength(200)] string OrganizationName,
    [Required, MaxLength(200)] string OrganizationNameAr,
    [MaxLength(2000)] string? Description,
    [MaxLength(256)] string? ContactEmail,
    [MaxLength(50)] string? ContactPhone,
    [MaxLength(2)] string? CountryCode,
    [Required, MaxLength(64)] string TimeZone,
    [Required, RegularExpression("^(en|ar)$")] string DefaultLanguage,
    [Required, EmailAddress, MaxLength(256)] string InitialAdministratorEmail,
    [Required, MaxLength(200)] string InitialAdministratorFullName);

public sealed record UpdateOrganizationRequest(
    [Required, MaxLength(200)] string OrganizationName,
    [Required, MaxLength(200)] string OrganizationNameAr,
    [MaxLength(2000)] string? Description,
    [MaxLength(256)] string? ContactEmail,
    [MaxLength(50)] string? ContactPhone,
    [MaxLength(2)] string? CountryCode,
    [Required, MaxLength(64)] string TimeZone,
    [Required, RegularExpression("^(en|ar)$")] string DefaultLanguage);

public sealed record UpdateOrganizationProfileRequest(
    [Required, MaxLength(200)] string OrganizationName,
    [Required, MaxLength(200)] string OrganizationNameAr,
    [MaxLength(2000)] string? Description,
    [MaxLength(256)] string? ContactEmail,
    [MaxLength(50)] string? ContactPhone,
    [Required, MaxLength(64)] string TimeZone,
    [Required, RegularExpression("^(en|ar)$")] string DefaultLanguage,
    bool RequireSecondAdminApprovalForSupportAccess = false,
    bool? AskClarifyingQuestions = null);

public sealed record SetOrganizationStatusRequest(
    [Required(ErrorMessage = "status_invalid"), RegularExpression("^(Active|Suspended|Deactivated)$", ErrorMessage = "status_invalid")] string Status,
    [MaxLength(500)] string? Reason);

/// <summary>Create/resend responses: the initial administrator's temporary password is emailed; DeliveredTo is the masked address.</summary>
public sealed record OrganizationProvisioningResponse(Guid OrganizationId, string DeliveredTo);
