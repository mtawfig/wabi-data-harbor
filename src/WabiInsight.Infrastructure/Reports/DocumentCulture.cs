using System.Globalization;

namespace WabiInsight.Infrastructure.Reports;

/// <summary>
/// How a document (PDF, Word, the chart images of Excel) writes numbers and month names. English documents use the
/// invariant culture. Arabic documents take their month names from Arabic and write numbers with Western digits, a
/// comma between thousands and a point before decimals: the same digits as the text the model writes in the document,
/// and the same on every server. The operating system's own Arabic number format is not used, because it differs
/// between Windows and Linux (Windows writes 8٬094, with Western digits and the Arabic thousands mark).
/// </summary>
public static class DocumentCulture
{
    private static readonly CultureInfo Arabic = BuildArabic();

    public static CultureInfo For(bool rtl) => rtl ? Arabic : CultureInfo.InvariantCulture;

    private static CultureInfo BuildArabic()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("ar-AE").Clone();
        culture.NumberFormat = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        return CultureInfo.ReadOnly(culture);
    }
}
