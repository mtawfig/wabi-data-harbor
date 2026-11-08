using System.Globalization;
using System.Text;

namespace WabiInsight.Core.Common;

/// <summary>
/// Numbers in e-mails as people read them, matching the web app: thousands separators and at most two decimals;
/// in Arabic the digits are Arabic-Indic with the Arabic thousands (٬) and decimal (٫) separators.
/// </summary>
public static class NumberText
{
    public static string Format(decimal value, bool arabic)
    {
        var text = value.ToString("#,0.##", CultureInfo.InvariantCulture);
        if (!arabic)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(c switch
            {
                >= '0' and <= '9' => (char)('٠' + (c - '0')),
                ',' => '٬',
                '.' => '٫',
                _ => c,
            });
        }

        return builder.ToString();
    }
}
