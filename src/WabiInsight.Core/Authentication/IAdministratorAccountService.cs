namespace WabiInsight.Core.Authentication;

public enum AdministratorAccountOutcome
{
    Success = 0,
    NotFound = 1,
    EmailAlreadyRegistered = 2,
    InvalidEmail = 3,
    SelfResetNotAllowed = 4,
}

/// <summary>
/// Result of creating or resetting an administrator. The temporary password is delivered by email only
/// (owner decision: administrators never see it); the API returns the masked recipient address.
/// </summary>
public sealed record AdministratorAccountResult(AdministratorAccountOutcome Outcome, Guid? AdministratorId, string? DeliveredTo, Guid? EmailMessageId);

public interface IAdministratorAccountService
{
    Task<AdministratorAccountResult> CreateAsync(string email, string fullName, string preferredLanguage, CancellationToken cancellationToken = default);

    Task<AdministratorAccountResult> ResetPasswordAsync(Guid targetAdministratorId, CancellationToken cancellationToken = default);
}
