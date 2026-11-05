namespace WabiInsight.Api.Contracts;

public sealed record PlatformInfoResponse(
    string ProductName,
    string ProductNameAr,
    string PlatformCode,
    string Tagline,
    string TaglineAr,
    string Version,
    IReadOnlyList<string> SupportedLanguages);
