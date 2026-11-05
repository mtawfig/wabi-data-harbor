using WabiInsight.Core.Platform;

namespace WabiInsight.UnitTests.Platform;

public class ProductInfoTests
{
    [Fact]
    public void ProductIdentity_MatchesSpecification()
    {
        Assert.Equal("Wabi Insight", ProductInfo.ProductName);
        Assert.Equal("AP-AIR", ProductInfo.PlatformCode);
        Assert.Equal("Ask Your Data. Trust the Answer.", ProductInfo.Tagline);
    }

    [Fact]
    public void SupportedLanguages_AreEnglishAndArabicOnly()
    {
        Assert.Equal(["en", "ar"], ProductInfo.SupportedLanguages);
    }

    [Fact]
    public void Version_IsSemanticVersion()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", ProductInfo.Version);
    }
}
