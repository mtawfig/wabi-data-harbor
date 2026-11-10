using System.Text.Json;
using System.Text.RegularExpressions;

namespace WabiInsight.Core.Ai;

/// <summary>
/// Finds the calendar dates written into a stored plan (P13.27). A plan asked as "last month" holds a relative
/// placeholder (<see cref="RelativeDates"/>) and follows the calendar at every run; a plan asked as "March 2026", or
/// saved before relative dates existed, holds the dates themselves and covers the same period every time. Whoever
/// schedules such an analysis as a Periodic Report is told so.
/// </summary>
public static partial class PlanDates
{
    public const int MaxReported = 6;

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2}(:\d{2}(\.\d+)?)?)?$")]
    private static partial Regex Literal();

    /// <summary>The distinct fixed dates (yyyy-MM-dd) among the plan's filter and condition values, earliest first.</summary>
    public static IReadOnlyList<string> FixedDates(string? planJson)
    {
        if (string.IsNullOrWhiteSpace(planJson))
        {
            return [];
        }

        var found = new SortedSet<string>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(planJson);
            Collect(document.RootElement, null, found);
        }
        catch (JsonException)
        {
            return [];
        }

        return [.. found.Take(MaxReported)];
    }

    private static void Collect(JsonElement element, string? property, SortedSet<string> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var member in element.EnumerateObject())
                {
                    Collect(member.Value, member.Name, found);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, property, found);
                }

                break;
            case JsonValueKind.String when IsValueProperty(property):
                var text = element.GetString()!.Trim();
                if (!RelativeDates.IsPlaceholder(text) && Literal().IsMatch(text) && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", out _))
                {
                    found.Add(text[..10]);
                }

                break;
        }
    }

    /// <summary>Only the values a condition compares with: titles and descriptions may mention a date without fixing the period.</summary>
    private static bool IsValueProperty(string? property)
        => property is not null && (property.Equals("value", StringComparison.OrdinalIgnoreCase) || property.Equals("values", StringComparison.OrdinalIgnoreCase)
            || property.Equals("valueTo", StringComparison.OrdinalIgnoreCase) || property.Equals("value2", StringComparison.OrdinalIgnoreCase));
}
