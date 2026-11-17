using WabiInsight.Core.Entities.Platform;

namespace WabiInsight.Core.Organizations;

public sealed record CreateOrganizationCommand(
    string OrganizationCode,
    string OrganizationName,
    string OrganizationNameAr,
    string? Description,
    string? ContactEmail,
    string? ContactPhone,
    string? CountryCode,
    string TimeZone,
    string DefaultLanguage,
    string InitialAdministratorEmail,
    string InitialAdministratorFullName);

public sealed record UpdateOrganizationCommand(
    string OrganizationName,
    string OrganizationNameAr,
    string? Description,
    string? ContactEmail,
    string? ContactPhone,
    string? CountryCode,
    string TimeZone,
    string DefaultLanguage);

/// <summary>Fields an Organization Administrator may change about their own organization (code and status are platform-only).</summary>
public sealed record UpdateOrganizationProfileCommand(
    string OrganizationName,
    string OrganizationNameAr,
    string? Description,
    string? ContactEmail,
    string? ContactPhone,
    string TimeZone,
    string DefaultLanguage,
    bool RequireSecondAdminApprovalForSupportAccess = false,
    bool? AskClarifyingQuestions = null);

public enum OrganizationOperationOutcome
{
    Success = 0,
    NotFound = 1,
    ValidationFailed = 2,
    CodeAlreadyExists = 3,
    EmailAlreadyRegistered = 4,
    InvalidStatusTransition = 5,
}

public sealed record OrganizationOperationResult(
    OrganizationOperationOutcome Outcome,
    IReadOnlyList<string> Errors,
    OrganizationDetail? Organization = null,
    string? DeliveredTo = null);

/// <summary>Platform-facing view: identity, status and aggregate counts only. Never application names or connection details (specification §5.2).</summary>
public sealed record OrganizationDetail(
    Guid OrganizationId,
    string OrganizationCode,
    string OrganizationName,
    string OrganizationNameAr,
    string? Description,
    string? ContactEmail,
    string? ContactPhone,
    string? CountryCode,
    string TimeZone,
    string DefaultLanguage,
    OrganizationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    int UserCount,
    int ActiveUserCount,
    int ApplicationCount,
    string? InitialAdministratorEmail,
    bool InitialAdministratorActivated);

public sealed record OrganizationListQuery(int Page = 1, int PageSize = 25, string? Search = null, OrganizationStatus? Status = null);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>Organization-facing view of the signed-in user's own organization.</summary>
public sealed record OrganizationProfile(
    Guid OrganizationId,
    string OrganizationCode,
    string OrganizationName,
    string OrganizationNameAr,
    string? Description,
    string? ContactEmail,
    string? ContactPhone,
    string? CountryCode,
    string TimeZone,
    string DefaultLanguage,
    OrganizationStatus Status,
    DateTimeOffset CreatedAt,
    bool RequireSecondAdminApprovalForSupportAccess = false,
    bool HasLogo = false,
    bool AskClarifyingQuestions = true);

public sealed record LogoUpload(string FileName, string ContentType, long Length, Stream Content);

public sealed record LogoDownload(Stream Content, string ContentType);

public interface IOrganizationService
{
    /// <summary>Platform administrators only. Creates the organization, provisions system roles and the built-in application, and emails the initial administrator a temporary password.</summary>
    Task<OrganizationOperationResult> CreateAsync(CreateOrganizationCommand command, CancellationToken cancellationToken = default);

    Task<OrganizationOperationResult> UpdateAsync(Guid organizationId, UpdateOrganizationCommand command, CancellationToken cancellationToken = default);

    Task<OrganizationOperationResult> SetStatusAsync(Guid organizationId, OrganizationStatus status, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Re-issues the initial administrator's temporary password by email while that account has not completed its first sign-in.</summary>
    Task<OrganizationOperationResult> ResendInitialAdministratorAsync(Guid organizationId, CancellationToken cancellationToken = default);

    Task<OrganizationDetail?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default);

    Task<PagedResult<OrganizationDetail>> ListAsync(OrganizationListQuery query, CancellationToken cancellationToken = default);

    /// <summary>The signed-in organization user's own organization (tenant context only).</summary>
    Task<OrganizationProfile> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>The signed-in organization user's own roles with their names in English and Arabic (profile page).</summary>
    Task<IReadOnlyList<MyRoleDto>> GetMyRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>Organization Administrators: replaces the logo used on report pages (PNG or JPEG, at most 1 MB).</summary>
    Task SetLogoAsync(LogoUpload upload, CancellationToken cancellationToken = default);

    Task RemoveLogoAsync(CancellationToken cancellationToken = default);

    /// <summary>The current organization's logo, or null when none is uploaded.</summary>
    Task<LogoDownload?> OpenLogoAsync(CancellationToken cancellationToken = default);

    /// <summary>Organization administrators only; updates the profile of the signed-in user's organization.</summary>
    Task<OrganizationOperationResult> UpdateCurrentAsync(UpdateOrganizationProfileCommand command, CancellationToken cancellationToken = default);
}

/// <summary>Creates organization users (with roles) and emails the temporary password. Shared by onboarding (P2.1) and user management (P2.2).</summary>
public interface IOrganizationUserProvisioningService
{
    Task<ProvisionedUser> ProvisionAsync(
        Guid organizationId,
        string email,
        string fullName,
        string preferredLanguage,
        IReadOnlyCollection<string> roleCodes,
        CancellationToken cancellationToken = default);

    /// <summary>Generates a new temporary password for an organization user, emails it and forces a change at next sign-in.</summary>
    Task<ProvisionedUser> ReissueTemporaryPasswordAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);
}

public sealed record ProvisionedUser(Guid UserId, string Email, string DeliveredTo, Guid EmailMessageId);

public sealed class OrganizationValidationException : Exception
{
    public OrganizationValidationException()
    {
    }

    public OrganizationValidationException(string message)
        : base(message)
    {
    }

    public OrganizationValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public OrganizationValidationException(OrganizationOperationOutcome outcome, IReadOnlyList<string> errors)
        : base(string.Join(", ", errors))
    {
        Outcome = outcome;
        Errors = errors;
    }

    public OrganizationOperationOutcome Outcome { get; } = OrganizationOperationOutcome.ValidationFailed;

    public IReadOnlyList<string> Errors { get; } = [];
}

/// <summary>One of the caller's own roles: the stable code and its display names.</summary>
public sealed record MyRoleDto(string RoleCode, string NameEn, string NameAr);
