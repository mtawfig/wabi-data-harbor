using System.ComponentModel.DataAnnotations;

namespace WabiInsight.Api.Contracts;

public sealed record LoginRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MaxLength(1024)] string Password);

public sealed record SessionResponse(
    Guid Id,
    string PrincipalType,
    string Email,
    string DisplayName,
    Guid? OrganizationId,
    IReadOnlyList<string> Roles,
    bool MustChangePassword,
    string PreferredLanguage);

public sealed record ChangePasswordRequest(
    [Required, MaxLength(1024)] string CurrentPassword,
    [Required, MaxLength(1024)] string NewPassword);

public sealed record CsrfTokenResponse(string Token, string HeaderName);

public sealed record BootstrapStatusResponse(
    bool BootstrapCompleted,
    DateTimeOffset? BootstrapCompletedAt,
    string? BootstrapAdministratorEmail,
    string? SchemaVersion,
    bool PendingMigrations,
    bool BootstrapEnabled);

public sealed record BootstrapInitializeRequest([Required, MaxLength(512)] string InitializationSecret);

public sealed record BootstrapInitializeResponse(Guid AdministratorId, string Email);

public sealed record CreateAdministratorRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MaxLength(200)] string FullName,
    [RegularExpression("^(en|ar)$")] string? PreferredLanguage);

/// <summary>The temporary password is emailed, never returned; DeliveredTo is the masked recipient address.</summary>
public sealed record AccountProvisioningResponse(Guid Id, string DeliveredTo, bool MustChangePassword, Guid? EmailMessageId);

public sealed record SetAdministratorStatusRequest(bool IsActive);

/// <summary>The caller's own interface language: "en" or "ar".</summary>
public sealed record SetLanguageRequest(string? Language);

/// <summary>Self-service reset, step 1: the sign-in e-mail of the account. The answer is the same for every address.</summary>
public sealed record ForgotPasswordRequest([Required, MaxLength(256)] string Email);

/// <summary>Self-service reset: the token of the link in the e-mail.</summary>
public sealed record ResetLinkRequest([Required, MaxLength(128)] string Token);

public sealed record ResetLinkStatusResponse(bool Valid);

/// <summary>Self-service reset, step 2: the link's token and the new password.</summary>
public sealed record ResetPasswordRequest(
    [Required, MaxLength(128)] string Token,
    [Required, MaxLength(1024)] string NewPassword);
