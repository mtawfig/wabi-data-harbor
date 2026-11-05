using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Authentication;

public interface IAuthenticationService
{
    /// <summary>Resolves the account category through LoginIdentities and verifies the password, lockout and status.</summary>
    Task<LoginResult> LoginAsync(string email, string password, string? ipAddress, CancellationToken cancellationToken = default);

    Task<ChangePasswordResult> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default);

    Task RecordLogoutAsync(CancellationToken cancellationToken = default);
}

/// <summary>Loads the stored state of a principal for session revalidation and the tenant context.</summary>
public interface IAccountResolver
{
    Task<ResolvedAccount> ResolveAsync(PrincipalType principalType, Guid principalId, CancellationToken cancellationToken = default);
}

/// <summary>Administrator-assisted reset of another Platform Administrator's password (specification §8).</summary>
public interface IAdministratorPasswordService
{
    /// <summary>Returns the one-time temporary password exactly once; it is never stored or logged in plain text.</summary>
    Task<string?> ResetPasswordAsync(Guid targetAdministratorId, CancellationToken cancellationToken = default);
}
