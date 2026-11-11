using System.Text.RegularExpressions;
using WabiInsight.Core.Ai;

namespace WabiInsight.Infrastructure.Reports;

/// <summary>
/// Table headers and chart legends of a panel in the document's language. In Arabic every result column is named from
/// the validated plan and the catalogue's Arabic display names ("إجمالي المبلغ"); in English the planner's alias reads well
/// already, so only a plain column with a catalogue name uses that name. Mirrors the web app's answer view.
/// </summary>
public static partial class ColumnHeaders
{
    private static readonly Dictionary<string, (string En, string Ar)> Aggregates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["count"] = ("Number of {0}", "عدد {0}"),
        ["count_distinct"] = ("Number of different {0}", "عدد قيم {0} المختلفة"),
        ["sum"] = ("Total {0}", "إجمالي {0}"),
        ["avg"] = ("Average {0}", "متوسط {0}"),
        ["min"] = ("Minimum {0}", "أدنى {0}"),
        ["max"] = ("Maximum {0}", "أعلى {0}"),
    };

    private static readonly Dictionary<string, string> UnitsAr = new(StringComparer.OrdinalIgnoreCase)
    {
        ["day"] = "اليوم", ["week"] = "الأسبوع", ["month"] = "الشهر", ["quarter"] = "ربع السنة", ["year"] = "السنة",
    };

    /// <summary>Result column name (the plan's output alias) → header, case-insensitive.</summary>
    public static IReadOnlyDictionary<string, string> For(AnalysisPanel panel, bool arabic)
    {
        ArgumentNullException.ThrowIfNull(panel);
        var labels = panel.Labels ?? new Dictionary<string, DisplayLabel>();
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in panel.Plan.Select)
        {
            if (item.Alias.Length == 0)
            {
                continue;
            }

            headers[item.Alias] = arabic ? Arabic(item, labels) : English(item, labels);
        }

        return headers;
    }

    /// <summary>The header of one result column, falling back to the column name in words.</summary>
    public static string Header(IReadOnlyDictionary<string, string> headers, string name)
        => headers.TryGetValue(name, out var header) ? header : QuestPdfReportRenderer.Label(name);

    /// <summary>The panel's series with their names as readers see them: a measure's header, or "header · value".</summary>
    public static IReadOnlyList<ResultSeries> Series(AnalysisPanel panel, bool arabic)
    {
        ArgumentNullException.ThrowIfNull(panel);
        var headers = For(panel, arabic);
        return panel.Result.Series.Select(s => s with { Name = SeriesName(headers, s.Name) }).ToList();
    }

    internal static string SeriesName(IReadOnlyDictionary<string, string> headers, string name)
    {
        var split = name.IndexOf(" · ", StringComparison.Ordinal);
        if (split > 0 && headers.TryGetValue(name[..split], out var measure))
        {
            return $"{measure} · {name[(split + 3)..]}";
        }

        return headers.TryGetValue(name, out var header) ? header : name;
    }

    private static string English(PlanSelectItem item, IReadOnlyDictionary<string, DisplayLabel> labels)
    {
        if (item.Aggregate is null && item.DateTrunc is null && item.Column is not null && labels.TryGetValue(item.Column, out var label) && !string.IsNullOrWhiteSpace(label.En))
        {
            return label.En.Trim();
        }

        return QuestPdfReportRenderer.Label(Humanize(item.Alias));
    }

    private static string Arabic(PlanSelectItem item, IReadOnlyDictionary<string, DisplayLabel> labels)
    {
        var aggregate = item.Aggregate?.ToLowerInvariant();
        if (aggregate is not null && Aggregates.TryGetValue(aggregate, out var template))
        {
            return aggregate == "count" && (item.Column is null || item.Column == "*")
                ? "عدد الصفوف"
                : string.Format(System.Globalization.CultureInfo.InvariantCulture, template.Ar, Column(item.Column, labels));
        }

        if (item.DateTrunc is { } unit && UnitsAr.TryGetValue(unit, out var unitAr))
        {
            return $"{Column(item.Column, labels)} حسب {unitAr}";
        }

        return item.Column is null ? QuestPdfReportRenderer.Label(Humanize(item.Alias)) : Column(item.Column, labels);
    }

    /// <summary>"t.PostedAt" → the catalogue's Arabic name, else its English name, else the column name in words.</summary>
    private static string Column(string? reference, IReadOnlyDictionary<string, DisplayLabel> labels)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return string.Empty;
        }

        if (labels.TryGetValue(reference, out var label))
        {
            var text = !string.IsNullOrWhiteSpace(label.Ar) ? label.Ar : label.En;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        var dot = reference.IndexOf('.', StringComparison.Ordinal);
        return QuestPdfReportRenderer.Label(Humanize(dot >= 0 ? reference[(dot + 1)..] : reference));
    }

    /// <summary>"HomeBranchCode" → "Home branch code"; "total_amount" → "total amount".</summary>
    internal static string Humanize(string name)
    {
        var spaced = CamelBoundary().Replace(name.Replace('_', ' '), "$1 $2").Trim();
        var words = spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select((w, i) => i > 0 && !(w.Length > 1 && w.All(c => char.IsUpper(c) || char.IsDigit(c))) ? w.ToLowerInvariant() : w);
        return string.Join(' ', words);
    }

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex CamelBoundary();
}
