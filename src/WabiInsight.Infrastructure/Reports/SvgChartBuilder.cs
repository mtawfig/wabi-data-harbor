using System.Globalization;
using System.Text;
using WabiInsight.Core.Ai;

namespace WabiInsight.Infrastructure.Reports;

/// <summary>A drawn chart and its final size: a chart grows taller when its category labels need room below the axis.</summary>
public sealed record ChartSvg(string Svg, double Width, double Height);

/// <summary>
/// Draws bar, line and pie charts as SVG from calculated <see cref="ResultSeries"/> for the PDF renderer.
/// No model text reaches the drawing: labels are the category values and series names from the result.
/// Category labels never overlap: they stay on one line when they fit, wrap onto two lines, or are drawn at an angle
/// (with the chart made taller to hold them); series names are shown as words ("Cash in", not "cash_in").
/// </summary>
public static class SvgChartBuilder
{
    public static readonly string[] Palette = ["#087DF2", "#22B8CF", "#0B1F3A", "#F59E0B", "#DC2626", "#64748B", "#7C3AED", "#059669"];

    private const string FontFamily = "Inter, 'IBM Plex Sans Arabic', sans-serif";
    private const int MaxCategories = 400;
    private const double LabelFont = 10;
    private const double LabelGap = 6;
    private const double LabelAngle = 45;
    private const int MaxLabelLength = 28;
    private const double LegendRow = 16;

    public static string Build(string type, IReadOnlyList<ResultSeries> series, double width = 640, double height = 300)
        => Render(type, series, width, height).Svg;

    /// <summary>Draws the chart; <paramref name="culture"/> names the months of monthly and yearly series (default invariant).</summary>
    public static ChartSvg Render(string type, IReadOnlyList<ResultSeries> series, double width = 640, double height = 300, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (series.Count == 0 || series[0].Categories.Count == 0)
        {
            return new ChartSvg(Empty(width, height), width, height);
        }

        var labels = CategoryLabels(series[0].Categories, culture ?? CultureInfo.InvariantCulture);
        return type switch
        {
            "pie" => new ChartSvg(Pie(series[0], labels, width, height), width, height),
            "line" => Cartesian(series, labels, width, height, line: true),
            _ => Cartesian(series, labels, width, height, line: false),
        };
    }

    /// <summary>
    /// Category labels as readers expect them: a series of first-of-month dates reads "Jan 2025" (a series of 1 January
    /// dates reads "2025"), and every other value is shown as it is. Arabic documents get "2025-01": the chart drawing
    /// cannot yet lay out Arabic text right to left, so month names stay out of their charts.
    /// </summary>
    internal static IReadOnlyList<string> CategoryLabels(IReadOnlyList<string> categories, CultureInfo culture)
    {
        var dates = new List<DateTime>(categories.Count);
        foreach (var category in categories)
        {
            var text = category.Length >= 10 ? category[..10] : category;
            if (category.Length is not (10 or 19) || (category.Length == 19 && !category.EndsWith("T00:00:00", StringComparison.Ordinal))
                || !DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date.Day != 1)
            {
                return categories;
            }

            dates.Add(date);
        }

        var yearly = dates.All(d => d.Month == 1) && dates.Select(d => d.Year).Distinct().Count() == dates.Count && dates.Count > 1;
        var arabic = culture.TwoLetterISOLanguageName == "ar";
        return dates.Select(d => yearly ? d.ToString("yyyy", CultureInfo.InvariantCulture) : d.ToString(arabic ? "yyyy-MM" : "MMM yyyy", arabic ? CultureInfo.InvariantCulture : culture)).ToList();
    }

    /// <summary>A column alias as words: "cash_in" reads "Cash in".</summary>
    internal static string SeriesLabel(string name) => QuestPdfReportRenderer.Label(name);

    private static string Empty(double width, double height)
        => $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(width)}\" height=\"{F(height)}\" viewBox=\"0 0 {F(width)} {F(height)}\"><text x=\"{F(width / 2)}\" y=\"{F(height / 2)}\" text-anchor=\"middle\" font-family=\"{FontFamily}\" font-size=\"12\" fill=\"#64748B\">No data</text></svg>";

    private enum LabelMode
    {
        OneLine,
        TwoLines,
        Angled,
    }

    private static ChartSvg Cartesian(IReadOnlyList<ResultSeries> input, IReadOnlyList<string> allLabels, double width, double height, bool line)
    {
        var series = input.Select(s => new ResultSeries(s.Name, s.Categories.Take(MaxCategories).ToList(), s.Values.Take(MaxCategories).ToList())).ToList();
        var categories = series[0].Categories;
        var labels = allLabels.Take(categories.Count).Select(l => l.Length > MaxLabelLength ? l[..(MaxLabelLength - 1)].TrimEnd() + "…" : l).ToList();
        var max = Math.Max(1e-9, series.SelectMany(s => s.Values).DefaultIfEmpty(0).Max());
        var min = Math.Min(0, series.SelectMany(s => s.Values).DefaultIfEmpty(0).Min());
        var (niceMin, niceMax, step) = NiceScale(min, max);
        var left = 56d;
        var right = 12d;
        var top = 14d;
        var plotWidth = width - left - right;
        var slot = plotWidth / categories.Count;

        // Legend: one entry per series, wrapped onto further rows when they do not fit across.
        var legend = new List<(string Text, double X, double Row)>();
        if (series.Count > 1)
        {
            var x = left;
            var row = 0;
            foreach (var s in series)
            {
                var text = TextMetrics.Fit(SeriesLabel(s.Name), LabelFont, 150);
                var itemWidth = 14 + TextMetrics.Width(text, LabelFont) + 12;
                if (x > left && x + itemWidth > width - right)
                {
                    row++;
                    x = left;
                }

                legend.Add((text, x, row));
                x += itemWidth;
            }
        }

        var legendHeight = legend.Count == 0 ? 0 : (legend[^1].Row + 1) * LegendRow + 4;

        // Category labels: one line when every label fits its slot; two lines when splitting at a space makes them fit (bars);
        // otherwise every label at an angle (bars), or every n-th label on one line (lines: a time axis reads well thinned out).
        var radians = LabelAngle * Math.PI / 180;
        var widest = labels.Select(l => TextMetrics.Width(l, LabelFont)).DefaultIfEmpty(0).Max();
        var wrapped = labels.Select(l => TwoLines(l, slot - LabelGap)).ToList();
        LabelMode mode;
        var every = 1;
        if (widest <= slot - LabelGap)
        {
            mode = LabelMode.OneLine;
        }
        else if (!line && wrapped.All(w => w is not null))
        {
            mode = LabelMode.TwoLines;
        }
        else if (line && widest <= 120)
        {
            mode = LabelMode.OneLine;
            every = Math.Max(1, (int)Math.Ceiling((widest + LabelGap * 2) / slot));
        }
        else
        {
            // Angled labels run down and to the left of their bar: the first ones need room before the axis.
            mode = LabelMode.Angled;
            left = Math.Max(left, widest * Math.Cos(radians) - slot / 2 + 8);
            plotWidth = width - left - right;
            slot = plotWidth / categories.Count;
            every = Math.Max(1, (int)Math.Ceiling(16 / slot));
        }

        var labelBand = mode switch
        {
            LabelMode.OneLine => 22d,
            LabelMode.TwoLines => 34d,
            _ => 16 + widest * Math.Sin(radians) + LabelFont * Math.Cos(radians),
        };

        // The plot keeps its height: the chart grows by whatever extra the labels and the legend need.
        var plotHeight = height - top - 40d;
        var bottom = labelBand + legendHeight + (legendHeight > 0 ? 6 : 0);
        var totalHeight = Math.Max(height, Math.Ceiling(top + plotHeight + bottom));
        double Y(double v) => top + plotHeight - (v - niceMin) / (niceMax - niceMin) * plotHeight;

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(width)}\" height=\"{F(totalHeight)}\" viewBox=\"0 0 {F(width)} {F(totalHeight)}\" font-family=\"{FontFamily}\">");

        // Grid and y labels.
        for (var v = niceMin; v <= niceMax + step / 2; v += step)
        {
            var y = Y(v);
            svg.Append(CultureInfo.InvariantCulture, $"<line x1=\"{F(left)}\" y1=\"{F(y)}\" x2=\"{F(width - right)}\" y2=\"{F(y)}\" stroke=\"#E2E8F0\" stroke-width=\"1\"/>");
            svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{F(left - 6)}\" y=\"{F(y + 4)}\" text-anchor=\"end\" font-size=\"10\" fill=\"#64748B\">{Esc(Compact(v))}</text>");
        }

        var labelTop = top + plotHeight + 14;
        for (var i = 0; i < categories.Count; i += every)
        {
            var x = left + slot * (i + 0.5);
            switch (mode)
            {
                case LabelMode.TwoLines:
                    var (first, second) = wrapped[i]!.Value;
                    svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{F(x)}\" y=\"{F(labelTop)}\" text-anchor=\"middle\" font-size=\"10\" fill=\"#64748B\">{Esc(first)}</text>");
                    if (second.Length > 0)
                    {
                        svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{F(x)}\" y=\"{F(labelTop + 12)}\" text-anchor=\"middle\" font-size=\"10\" fill=\"#64748B\">{Esc(second)}</text>");
                    }

                    break;
                case LabelMode.Angled:
                    var ax = x + 3;
                    var ay = top + plotHeight + 10;
                    svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{F(ax)}\" y=\"{F(ay)}\" text-anchor=\"end\" font-size=\"10\" fill=\"#64748B\" transform=\"rotate(-{F(LabelAngle)} {F(ax)} {F(ay)})\">{Esc(labels[i])}</text>");
                    break;
                default:
                    svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{F(x)}\" y=\"{F(labelTop)}\" text-anchor=\"middle\" font-size=\"10\" fill=\"#64748B\">{Esc(labels[i])}</text>");
                    break;
            }
        }

        svg.Append(CultureInfo.InvariantCulture, $"<line x1=\"{F(left)}\" y1=\"{F(Y(Math.Max(niceMin, 0)))}\" x2=\"{F(width - right)}\" y2=\"{F(Y(Math.Max(niceMin, 0)))}\" stroke=\"#CBD5E1\" stroke-width=\"1\"/>");

        if (line)
        {
            for (var s = 0; s < series.Count; s++)
            {
                var color = Palette[s % Palette.Length];
                var points = string.Join(" ", series[s].Values.Select((v, i) => $"{F(left + slot * (i + 0.5))},{F(Y(v))}"));
                svg.Append(CultureInfo.InvariantCulture, $"<polyline fill=\"none\" stroke=\"{color}\" stroke-width=\"2\" stroke-linejoin=\"round\" points=\"{points}\"/>");
                for (var i = 0; i < series[s].Values.Count; i++)
                {
                    svg.Append(CultureInfo.InvariantCulture, $"<circle cx=\"{F(left + slot * (i + 0.5))}\" cy=\"{F(Y(series[s].Values[i]))}\" r=\"2.5\" fill=\"{color}\"/>");
                }
            }
        }
        else
        {
            var group = slot * 0.72;
            var barWidth = Math.Min(36, group / series.Count);
            var groupWidth = barWidth * series.Count;
            for (var i = 0; i < categories.Count; i++)
            {
                for (var s = 0; s < series.Count; s++)
                {
                    var v = i < series[s].Values.Count ? series[s].Values[i] : 0;
                    var x = left + slot * (i + 0.5) - groupWidth / 2 + barWidth * s;
                    var yTop = Y(Math.Max(v, 0));
                    var h = Math.Abs(Y(v) - Y(0));
                    svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{F(x)}\" y=\"{F(yTop)}\" width=\"{F(Math.Max(1, barWidth - 2))}\" height=\"{F(h)}\" rx=\"2\" fill=\"{Palette[s % Palette.Length]}\"/>");
                }
            }
        }

        var legendTop = top + plotHeight + labelBand + 14;
        for (var s = 0; s < legend.Count; s++)
        {
            var (text, x, row) = legend[s];
            var y = legendTop + row * LegendRow;
            svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{F(x)}\" y=\"{F(y - 9)}\" width=\"10\" height=\"10\" rx=\"2\" fill=\"{Palette[s % Palette.Length]}\"/>");
            svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{F(x + 14)}\" y=\"{F(y)}\" font-size=\"10\" fill=\"#334155\">{Esc(text)}</text>");
        }

        svg.Append("</svg>");
        return new ChartSvg(svg.ToString(), width, totalHeight);
    }

    /// <summary>The label split at a space into two lines that each fit <paramref name="maxWidth"/>, or null when no split fits.</summary>
    private static (string First, string Second)? TwoLines(string label, double maxWidth)
    {
        if (TextMetrics.Width(label, LabelFont) <= maxWidth)
        {
            return (label, string.Empty);
        }

        var words = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var cut = words.Length - 1; cut >= 1; cut--)
        {
            var first = string.Join(' ', words[..cut]);
            var second = string.Join(' ', words[cut..]);
            if (TextMetrics.Width(first, LabelFont) <= maxWidth && TextMetrics.Width(second, LabelFont) <= maxWidth)
            {
                return (first, second);
            }
        }

        return null;
    }

    private static string Pie(ResultSeries series, IReadOnlyList<string> labels, double width, double height)
    {
        var total = series.Values.Sum();
        if (total <= 0)
        {
            return Empty(width, height);
        }

        var cx = height / 2;
        var cy = height / 2;
        var r = height / 2 - 12;
        var inner = r * 0.55;
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(width)}\" height=\"{F(height)}\" viewBox=\"0 0 {F(width)} {F(height)}\" font-family=\"{FontFamily}\">");
        var angle = -Math.PI / 2;
        var legendY = 18d;
        for (var i = 0; i < series.Values.Count; i++)
        {
            var share = series.Values[i] / total;
            var sweep = share * Math.PI * 2;
            var end = angle + sweep;
            var color = Palette[i % Palette.Length];
            if (share >= 0.9999)
            {
                svg.Append(CultureInfo.InvariantCulture, $"<circle cx=\"{F(cx)}\" cy=\"{F(cy)}\" r=\"{F(r)}\" fill=\"{color}\"/><circle cx=\"{F(cx)}\" cy=\"{F(cy)}\" r=\"{F(inner)}\" fill=\"#FFFFFF\"/>");
            }
            else if (sweep > 0)
            {
                var large = sweep > Math.PI ? 1 : 0;
                var path = $"M {F(cx + r * Math.Cos(angle))} {F(cy + r * Math.Sin(angle))} A {F(r)} {F(r)} 0 {large} 1 {F(cx + r * Math.Cos(end))} {F(cy + r * Math.Sin(end))} L {F(cx + inner * Math.Cos(end))} {F(cy + inner * Math.Sin(end))} A {F(inner)} {F(inner)} 0 {large} 0 {F(cx + inner * Math.Cos(angle))} {F(cy + inner * Math.Sin(angle))} Z";
                svg.Append(CultureInfo.InvariantCulture, $"<path d=\"{path}\" fill=\"{color}\" stroke=\"#FFFFFF\" stroke-width=\"1\"/>");
            }

            if (i < 12)
            {
                svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{F(height + 8)}\" y=\"{F(legendY - 9)}\" width=\"10\" height=\"10\" rx=\"2\" fill=\"{color}\"/>");
                var figures = $" · {Compact(series.Values[i])} ({share.ToString("P1", CultureInfo.InvariantCulture)})";
                var name = TextMetrics.Fit(labels[i], LabelFont, Math.Max(40, width - height - 26 - TextMetrics.Width(figures, LabelFont)));
                svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{F(height + 22)}\" y=\"{F(legendY)}\" font-size=\"10\" fill=\"#334155\">{Esc(name + figures)}</text>");
                legendY += 16;
            }

            angle = end;
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static (double Min, double Max, double Step) NiceScale(double min, double max)
    {
        var range = max - min;
        if (range <= 0)
        {
            range = Math.Abs(max) > 0 ? Math.Abs(max) : 1;
        }

        var rough = range / 5;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var residual = rough / magnitude;
        var step = residual switch { <= 1 => 1, <= 2 => 2, <= 2.5 => 2.5, <= 5 => 5, _ => 10 } * magnitude;
        var niceMin = Math.Floor(min / step) * step;
        var niceMax = Math.Ceiling(max / step) * step;
        if (niceMax <= niceMin)
        {
            niceMax = niceMin + step;
        }

        return (niceMin, niceMax, step);
    }

    /// <summary>1.35B, 1.2M, 340K, 12.5 — compact axis labels.</summary>
    public static string Compact(double value)
    {
        var abs = Math.Abs(value);
        return abs switch
        {
            >= 1_000_000_000 => (value / 1_000_000_000).ToString("0.##", CultureInfo.InvariantCulture) + "B",
            >= 1_000_000 => (value / 1_000_000).ToString("0.#", CultureInfo.InvariantCulture) + "M",
            >= 10_000 => (value / 1_000).ToString("0.#", CultureInfo.InvariantCulture) + "K",
            >= 100 => value.ToString("#,0", CultureInfo.InvariantCulture),
            _ => value.ToString("0.##", CultureInfo.InvariantCulture),
        };
    }

    private static string F(double value) => Math.Round(value, 2).ToString(CultureInfo.InvariantCulture);

    private static string Esc(string value) => value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
}
