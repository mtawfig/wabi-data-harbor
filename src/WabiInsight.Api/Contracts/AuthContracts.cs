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

public sealed record TemporaryPasswordResponse(string TemporaryPassword, bool MustChangePassword);
