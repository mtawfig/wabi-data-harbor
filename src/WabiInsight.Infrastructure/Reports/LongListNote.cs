using System.Globalization;
using WabiInsight.Core.Ai;

namespace WabiInsight.Infrastructure.Reports;

/// <summary>
/// The one line under a table that shows fewer rows than its list has (P13.50): how many rows are in the table, how
/// many the list has when that is known, and where the whole list is. It states numbers only; it never calls the
/// list partial or a sample. Null when the table shows the whole list.
/// </summary>
public static class LongListNote
{
    public static string? Text(AnalysisResult result, int shownRows, bool rtl, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(result);
        var available = result.Rows.Count;
        if (available <= shownRows && !result.Truncated)
        {
            return null;
        }

        var shown = Math.Min(shownRows, available).ToString("#,0", culture);
        var total = result.WholeListRows is { } rows && rows > Math.Min(shownRows, available) ? rows.ToString("#,0", culture) : null;
        if (rtl)
        {
            return total is null ? $"أول {shown} صف. القائمة الكاملة متوفرة في Excel." : $"أول {shown} صف من أصل {total}. القائمة الكاملة متوفرة في Excel.";
        }

        return total is null ? $"First {shown} rows. The full list is available in Excel." : $"First {shown} rows of {total}. The full list is available in Excel.";
    }
}
