namespace WabiInsight.Infrastructure.Reports;

/// <summary>
/// Approximate text widths for laying out report tables and chart labels before drawing (Inter for Latin text, IBM Plex
/// Sans Arabic for Arabic). The estimates lean wide on purpose: a column sized from them is never too narrow for its
/// numbers, and a chart label judged to fit does fit.
/// </summary>
internal static class TextMetrics
{
    /// <summary>The width of <paramref name="text"/> in points (or pixels) at <paramref name="fontSize"/>.</summary>
    public static double Width(string? text, double fontSize, bool bold = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var em = 0d;
        foreach (var c in text)
        {
            em += Advance(c);
        }

        return em * fontSize * (bold ? 1.06 : 1);
    }

    /// <summary>The longest single word (split on spaces) of <paramref name="text"/>: the narrowest a wrapped cell may be.</summary>
    public static double LongestWord(string? text, double fontSize, bool bold = false)
        => string.IsNullOrWhiteSpace(text) ? 0 : text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Max(w => Width(w, fontSize, bold));

    /// <summary>Shortens <paramref name="text"/> with an ellipsis until it fits in <paramref name="maxWidth"/>.</summary>
    public static string Fit(string text, double fontSize, double maxWidth)
    {
        if (Width(text, fontSize) <= maxWidth)
        {
            return text;
        }

        var length = text.Length;
        while (length > 1 && Width(text[..length] + "…", fontSize) > maxWidth)
        {
            length--;
        }

        return text[..length].TrimEnd() + "…";
    }

    private static double Advance(char c) => c switch
    {
        >= '0' and <= '9' => 0.62,
        ',' or '.' or ':' or ';' or '\'' or '|' or '!' => 0.30,
        ' ' => 0.29,
        '-' or '(' or ')' or '[' or ']' or '/' or '\\' => 0.40,
        '—' or '%' or '@' => 0.92,
        '–' or '…' => 0.62,
        'i' or 'j' or 'l' or 'I' or 'f' or 't' or 'r' => 0.36,
        'm' or 'w' or 'M' or 'W' => 0.90,
        >= 'A' and <= 'Z' => 0.72,
        >= 'a' and <= 'z' => 0.58,
        >= '؀' and <= 'ۿ' => 0.56,
        >= 'ݐ' and <= 'ݿ' => 0.56,
        >= 'ﭐ' and <= '﻿' => 0.56,
        _ => 0.64,
    };
}
