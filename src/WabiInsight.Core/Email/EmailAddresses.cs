namespace WabiInsight.Core.Email;

/// <summary>
/// Addresses that can never receive mail (P13.55): the domains reserved for documentation and testing (RFC 2606 and
/// RFC 6761). Sample organizations use them for their users, so a real mail server refuses every message to them.
/// </summary>
public static class EmailAddresses
{
    private static readonly string[] ReservedTopLevel = ["example", "test", "invalid", "localhost"];
    private static readonly string[] ReservedDomains = ["example.com", "example.net", "example.org"];

    public static bool IsReserved(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var at = email.LastIndexOf('@');
        var domain = (at < 0 ? email : email[(at + 1)..]).Trim().TrimEnd('.').ToLowerInvariant();
        if (domain.Length == 0)
        {
            return false;
        }

        var lastDot = domain.LastIndexOf('.');
        var topLevel = lastDot < 0 ? domain : domain[(lastDot + 1)..];
        return ReservedTopLevel.Contains(topLevel, StringComparer.Ordinal)
            || ReservedDomains.Any(reserved => domain == reserved || domain.EndsWith("." + reserved, StringComparison.Ordinal));
    }
}
