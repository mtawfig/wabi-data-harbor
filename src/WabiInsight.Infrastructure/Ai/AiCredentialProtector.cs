using Microsoft.AspNetCore.DataProtection;

namespace WabiInsight.Infrastructure.Ai;

/// <summary>Data Protection wrapper for provider API keys (same key ring as connection strings, separate purpose).</summary>
public sealed class AiCredentialProtector
{
    public const string Purpose = "WabiInsight.Ai.ProviderCredential.v1";

    private readonly IDataProtector _protector;

    public AiCredentialProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string apiKey) => _protector.Protect(apiKey);

    public string Unprotect(string payload) => _protector.Unprotect(payload);

    public static string Hint(string apiKey) => apiKey.Length <= 6 ? "••••" : $"…{apiKey[^4..]}";
}
