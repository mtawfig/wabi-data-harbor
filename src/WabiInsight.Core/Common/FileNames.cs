using System.Globalization;
using System.Text;

namespace WabiInsight.Core.Common;

/// <summary>
/// File names made from titles (P13.55). Letters and digits of every script are kept, so an Arabic title gives an
/// Arabic file name instead of only its digits or a generic word; everything else becomes a dash. The result holds no
/// character a file system, a browser or a mail client refuses.
/// </summary>
public static class FileNames
{
    public const int MaxLength = 60;

    /// <summary>The title as the stem of a file name; <paramref name="fallback"/> when nothing of it can be kept.</summary>
    public static string Slug(string? title, string fallback)
    {
        var slug = new StringBuilder();
        foreach (var c in (title ?? string.Empty).Normalize(NormalizationForm.FormC))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.Format || c == 'ـ')
            {
                continue; // Arabic vowel marks, the tatweel and invisible direction marks add nothing to a name.
            }

            if (char.IsLetterOrDigit(c))
            {
                slug.Append(char.ToLowerInvariant(c));
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var text = slug.ToString().Trim('-');
        if (text.Length > MaxLength)
        {
            // Cut at the end of the last whole word that fits (when that keeps at least half), never inside a word
            // and never between the two halves of a character outside the basic plane.
            var length = char.IsHighSurrogate(text[MaxLength - 1]) ? MaxLength - 1 : MaxLength;
            var lastWord = text.LastIndexOf('-', length);
            text = text[..(text[length] != '-' && lastWord >= MaxLength / 2 ? lastWord : length)].TrimEnd('-');
        }

        return text.Length == 0 ? fallback : text;
    }
}
