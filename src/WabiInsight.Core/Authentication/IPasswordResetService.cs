namespace WabiInsight.Core.Authentication;

/// <summary>
/// Self-service password reset (P13.32): a person who forgot the password asks for a link by e-mail and chooses a new
/// password on the page the link opens. Nobody is signed in on either step.
/// </summary>
public interface IPasswordResetService
{
    /// <summary>
    /// Sends a reset link to the account with this sign-in e-mail, when there is one that may sign in. Returns nothing
    /// either way: the caller must not learn whether the address belongs to an account.
    /// </summary>
    Task RequestAsync(string email, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>True while the link's token can still be used. Changes nothing.</summary>
    Task<bool> IsValidAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Sets the new password with the link's token, ends every open session of the account and spends the link.</summary>
    Task<PasswordResetResult> CompleteAsync(string token, string newPassword, CancellationToken cancellationToken = default);
}

public enum PasswordResetOutcome
{
    Success = 0,

    /// <summary>Unknown, used, replaced or expired link, or an account that may no longer sign in.</summary>
    LinkInvalid = 1,

    /// <summary>The new password breaks the password rules; the link stays usable.</summary>
    PolicyViolation = 2,
}

public sealed record PasswordResetResult(PasswordResetOutcome Outcome, IReadOnlyList<string> Errors)
{
    public static PasswordResetResult Succeeded { get; } = new(PasswordResetOutcome.Success, []);

    public static PasswordResetResult Invalid { get; } = new(PasswordResetOutcome.LinkInvalid, ["reset_link_invalid"]);
}
