using Microsoft.AspNetCore.Identity;
using WabiInsight.Core.Security;

namespace WabiInsight.Infrastructure.Security;

/// <summary>ASP.NET Core Identity PasswordHasher (PBKDF2-HMAC-SHA512, v3 format). No custom cryptography.</summary>
public sealed class IdentityPasswordHashingService : IPasswordHashingService
{
    private readonly PasswordHasher<object> _hasher = new();
    private static readonly object User = new();

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return _hasher.HashPassword(User, password);
    }

    public PasswordVerification Verify(string hashedPassword, string providedPassword)
    {
        if (string.IsNullOrEmpty(hashedPassword) || string.IsNullOrEmpty(providedPassword))
        {
            return PasswordVerification.Failed;
        }

        return _hasher.VerifyHashedPassword(User, hashedPassword, providedPassword) switch
        {
            PasswordVerificationResult.Success => PasswordVerification.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordVerification.SuccessRehashNeeded,
            _ => PasswordVerification.Failed,
        };
    }
}
