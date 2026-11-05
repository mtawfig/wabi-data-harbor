namespace WabiInsight.Core.Security;

/// <summary>Login emails are compared in normalized form (trimmed, upper-invariant), matching ASP.NET Core Identity.</summary>
public static class EmailNormalizer
{
    public static string Normalize(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim().ToUpperInvariant();
    }

    public static bool IsPlausible(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256)
        {
            return false;
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && at < email.Length - 1 && email.IndexOf('@', at + 1) < 0 && !email.Any(char.IsWhiteSpace);
    }
}
