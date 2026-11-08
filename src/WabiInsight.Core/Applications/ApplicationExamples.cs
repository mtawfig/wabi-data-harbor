using System.Text.Json;
using System.Text.RegularExpressions;
using WabiInsight.Core.Organizations;

namespace WabiInsight.Core.Applications;

/// <summary>
/// Example questions of an application: plain text in English and/or Arabic, written by the organization's administrators
/// and shown as chips on the Ask page. They are suggestions only: a chip fills the question box and the question then
/// goes through the same planning, validation and authorization as any typed question.
/// </summary>
public static partial class ApplicationExamples
{
    public const int MaxCount = 6;
    public const int MaxLength = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<ApplicationExampleQuestion> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ApplicationExampleQuestion>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string Serialize(IReadOnlyList<ApplicationExampleQuestion> examples) => JsonSerializer.Serialize(examples, Json);

    /// <summary>Trims, collapses whitespace and control characters, drops empty rows, and enforces the count and length limits.</summary>
    public static IReadOnlyList<ApplicationExampleQuestion> Normalize(IReadOnlyList<ApplicationExampleQuestion>? examples)
    {
        var cleaned = new List<ApplicationExampleQuestion>();
        foreach (var example in examples ?? [])
        {
            var en = Clean(example?.En);
            var ar = Clean(example?.Ar);
            if (en is null && ar is null)
            {
                continue;
            }

            if ((en?.Length ?? 0) > MaxLength || (ar?.Length ?? 0) > MaxLength)
            {
                throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "application_example_too_long");
            }

            cleaned.Add(new ApplicationExampleQuestion(en, ar));
        }

        if (cleaned.Count > MaxCount)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "application_examples_too_many");
        }

        return cleaned;
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = Whitespace().Replace(ControlCharacters().Replace(value, " "), " ").Trim();
        return text.Length == 0 ? null : text;
    }

    [GeneratedRegex(@"[\p{Cc}\p{Cf}]")]
    private static partial Regex ControlCharacters();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
