namespace WabiInsight.Core.Security;

public enum PasswordVerification
{
    Failed = 0,
    Success = 1,

    /// <summary>Correct password stored with an older algorithm/iteration count; the caller should re-hash.</summary>
    SuccessRehashNeeded = 2,
}

/// <summary>Framework-backed password hashing (ASP.NET Core Identity PasswordHasher). No custom cryptography.</summary>
public interface IPasswordHashingService
{
    string Hash(string password);

    PasswordVerification Verify(string hashedPassword, string providedPassword);
}
