namespace WabiInsight.Core.Platform;

/// <summary>
/// Immutable product identity. The product name and platform code are non-negotiable
/// specification values (see docs/WABI_INSIGHT_SPECIFICATION.md §34).
/// </summary>
public static class ProductInfo
{
    public const string ProductName = "Wabi Insight";

    public const string ProductNameArabic = "وابي إنسايت";

    public const string PlatformCode = "AP-AIR";

    public const string Tagline = "Ask Your Data. Trust the Answer.";

    public const string TaglineArabic = "اسأل بياناتك. وثق بالإجابة.";

    public static readonly IReadOnlyList<string> SupportedLanguages = ["en", "ar"];

    public static string Version =>
        typeof(ProductInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
