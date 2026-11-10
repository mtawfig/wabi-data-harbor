using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WabiInsight.Core.Platform;

/// <summary>
/// What a Platform Administrator may read of an audit row that belongs to an organization when they hold no approved
/// support grant for it (P13.26, specification §16: without a grant, no organization configuration). The row still
/// says what happened, when and to which record; what it would reveal about the organization's configuration is
/// withheld: data-source hints, table and column names, role and application names, analysis titles, e-mail
/// addresses, file names and free text. Kept are numbers, flags, identifiers, dates and a fixed list of code fields.
/// The names of the withheld fields are listed, so the reader knows that more exists.
/// </summary>
public static partial class AuditDetailReducer
{
    public const string WithheldKey = "withheld";

    /// <summary>
    /// Fields whose values are stable codes of the platform, never names an organization chose. "code" is not one of
    /// them: it also carries role and application codes, which an organization defines.
    /// </summary>
    private static readonly HashSet<string> CodeFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "action", "status", "outcome", "kind", "severity", "format", "scope", "providerType", "databaseType",
        "classification", "contentType", "ticketType", "impact", "preferredLanguage", "recurrence", "logo", "operation",
        "reason", "referenceNumber", "templateCode", "lookup", "previous", "next", "types",
    };

    [GeneratedRegex(@"^[A-Za-z0-9_.:\-]{1,64}$")]
    private static partial Regex CodeLike();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+\-]\d{2}:\d{2})?)?$")]
    private static partial Regex DateLike();

    /// <summary>Returns the reduced details and whether anything was withheld. Unreadable details are withheld whole.</summary>
    public static (string? Json, bool Reduced) Reduce(string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return (detailsJson, false);
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(detailsJson);
        }
        catch (JsonException)
        {
            return (null, true);
        }

        var withheld = new SortedSet<string>(StringComparer.Ordinal);
        var kept = Keep(root, null, withheld);
        if (withheld.Count == 0)
        {
            return (detailsJson, false);
        }

        var result = kept as JsonObject ?? [];
        result[WithheldKey] = new JsonArray([.. withheld.Select(name => (JsonNode)JsonValue.Create(name)!)]);
        return (result.ToJsonString(), true);
    }

    /// <summary>An entity id is shown when it is an identifier, not a name.</summary>
    public static string? ReduceEntityId(string? entityId)
        => entityId is null || Guid.TryParse(entityId, out _) ? entityId : null;

    private static JsonNode? Keep(JsonNode? node, string? field, SortedSet<string> withheld)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject source:
                var target = new JsonObject();
                foreach (var (name, value) in source)
                {
                    var before = withheld.Count;
                    var kept = Keep(value, name, withheld);
                    if (kept is not null || value is null)
                    {
                        target[name] = kept;
                    }
                    else if (withheld.Count == before)
                    {
                        withheld.Add(name);
                    }
                }

                return target;
            case JsonArray array:
                var items = new JsonArray();
                foreach (var item in array)
                {
                    var kept = Keep(item, field, withheld);
                    if (kept is not null)
                    {
                        items.Add(kept);
                    }
                    else if (item is not null && field is not null)
                    {
                        withheld.Add(field);
                    }
                }

                return items;
            case JsonValue value:
                return value.GetValueKind() switch
                {
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.DeepClone(),
                    JsonValueKind.String when IsShown(field, value.GetValue<string>()) => value.DeepClone(),
                    _ => null,
                };
            default:
                return null;
        }
    }

    private static bool IsShown(string? field, string text)
        => Guid.TryParse(text, out _)
           || DateLike().IsMatch(text)
           || (field is not null && CodeFields.Contains(field) && CodeLike().IsMatch(text));
}
