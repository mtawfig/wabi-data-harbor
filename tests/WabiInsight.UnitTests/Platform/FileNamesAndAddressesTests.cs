using WabiInsight.Core.Common;
using WabiInsight.Core.Email;

namespace WabiInsight.UnitTests.Platform;

public sealed class FileNamesAndAddressesTests
{
    [Theory]
    [InlineData("Sharjah Airport 2025: Punctuality Study", "sharjah-airport-2025-punctuality-study")]
    [InlineData("عرض جميع رحلات العربية للطيران في يناير 2025", "عرض-جميع-رحلات-العربية-للطيران-في-يناير-2025")]
    [InlineData("رَحَلاتُ الـعـربيّة ٢٠٢٥", "رحلات-العربية-٢٠٢٥")]
    [InlineData("Payroll / تكلفة الرواتب: 2025?", "payroll-تكلفة-الرواتب-2025")]
    [InlineData("  ..\\..//etc:passwd*  ", "etc-passwd")]
    [InlineData("—  ؟!", "analysis")]
    [InlineData("", "analysis")]
    public void Slug_KeepsLettersAndDigitsOfEveryScript_AndNothingAFileSystemRefuses(string title, string expected)
        => Assert.Equal(expected, FileNames.Slug(title, "analysis"));

    [Fact]
    public void Slug_IsBounded_AndNeverEndsWithADash()
    {
        var slug = FileNames.Slug(string.Join(' ', Enumerable.Repeat("تقرير", 40)), "analysis");
        Assert.True(slug.Length <= FileNames.MaxLength);
        Assert.False(slug.EndsWith('-'));
        // A long title is cut after a whole word; one long word without a break is cut at the limit.
        Assert.Equal("عرض-جميع-رحلات-العربية-للطيران-خلال-شهر-يناير-2025-مع-رقم", FileNames.Slug("عرض جميع رحلات العربية للطيران خلال شهر يناير 2025 مع رقم الرحلة والتاريخ والاتجاه", "analysis"));
        Assert.Equal(new string('a', FileNames.MaxLength), FileNames.Slug(new string('a', 100), "analysis"));
    }

    [Theory]
    [InlineData("khalid.alsuwaidi@gulfaviation.example", true)]
    [InlineData("w1001@wabibank.example", true)]
    [InlineData("someone@org.test", true)]
    [InlineData("a@b.invalid", true)]
    [InlineData("root@localhost", true)]
    [InlineData("User@Example.COM", true)]
    [InlineData("user@mail.example.org.", true)]
    [InlineData("support@wabitex.com", false)]
    [InlineData("someone@examples.com", false)]
    [InlineData("someone@contest.ae", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsReserved_RecognisesTheDomainsThatCanNeverReceiveMail(string? email, bool expected)
        => Assert.Equal(expected, EmailAddresses.IsReserved(email));
}
