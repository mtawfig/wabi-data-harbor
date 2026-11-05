using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Authentication;

public enum LoginOutcome
{
    Success = 0,
    InvalidCredentials = 1,
    LockedOut = 2,
    AccountDisabled = 3,
    OrganizationSuspended = 4,
}

/// <summary>Everything the cookie needs: the trusted principal plus the session-validation data.</summary>
public sealed record AuthenticatedAccount(
    TenantPrincipal Principal,
    string Email,
    string SecurityStamp,
    bool MustChangePassword,
    string PreferredLanguage);

public sealed record LoginResult(LoginOutcome Outcome, AuthenticatedAccount? Account, DateTimeOffset? LockoutEnd = null)
{
    public static LoginResult Failed(LoginOutcome outcome, DateTimeOffset? lockoutEnd = null) => new(outcome, null, lockoutEnd);
}

public enum ChangePasswordOutcome
{
    Success = 0,
    CurrentPasswordInvalid = 1,
    PolicyViolation = 2,
}

public sealed record ChangePasswordResult(ChangePasswordOutcome Outcome, IReadOnlyList<string> Errors, AuthenticatedAccount? Account);

/// <summary>Why a stored account can no longer use its session (checked on security-stamp revalidation).</summary>
public enum AccountDenyReason
{
    None = 0,
    NotFound = 1,
    Inactive = 2,
    OrganizationSuspended = 3,
    LockedOut = 4,
}

public sealed record ResolvedAccount(AuthenticatedAccount? Account, AccountDenyReason DenyReason);
