namespace WabiInsight.Core.Authentication;

public enum AdministratorAccountOutcome
{
    Success = 0,
    NotFound = 1,
    EmailAlreadyRegistered = 2,
    InvalidEmail = 3,
    SelfResetNotAllowed = 4,
    LastActiveAdministrator = 5,
}

/// <summary>
/// Result of creating or resetting an administrator. The temporary password is delivered by email only
/// (owner decision: administrators never see it); the API returns the masked recipient address.
/// </summary>
public sealed record AdministratorAccountResult(AdministratorAccountOutcome Outcome, Guid? AdministratorId, string? DeliveredTo, Guid? EmailMessageId);

public sealed record AdministratorDto(Guid AdministratorId, string Email, string FullName, bool IsActive, bool MustChangePassword, bool IsBootstrapAdministrator, string PreferredLanguage, DateTimeOffset? LastLoginAt, DateTimeOffset CreatedAt);

public interface IAdministratorAccountService
{
    Task<IReadOnlyList<AdministratorDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<AdministratorAccountResult> CreateAsync(string email, string fullName, string preferredLanguage, CancellationToken cancellationToken = default);

    Task<AdministratorAccountResult> ResetPasswordAsync(Guid targetAdministratorId, CancellationToken cancellationToken = default);

    /// <summary>Activates or deactivates another administrator. Self-deactivation is refused and at least one active administrator must remain.</summary>
    Task<AdministratorAccountResult> SetActiveAsync(Guid targetAdministratorId, bool isActive, CancellationToken cancellationToken = default);
}
