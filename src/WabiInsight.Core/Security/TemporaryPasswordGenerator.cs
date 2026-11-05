using System.Security.Cryptography;

namespace WabiInsight.Core.Security;

/// <summary>Cryptographically random temporary passwords for administrator-assisted resets.</summary>
public static class TemporaryPasswordGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789-_!#";

    public static string Generate(int length = 20)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 12);
        return string.Create(length, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }
        });
    }

    public static string NewSecurityStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
}
