using System.Globalization;
using System.Text;
using WabiInsight.Core.Ai;

namespace WabiInsight.Infrastructure.Reports;

/// <summary>
/// Draws bar, line and pie charts as SVG from calculated <see cref="ResultSeries"/> for the PDF renderer.
/// No model text reaches the drawing: labels are the category values and series names from the result.
/// </summary>
public static class SvgChartBuilder
{
    public static readonly string[] Palette = ["#087DF2", "#22B8CF", "#0B1F3A", "#F59E0B", "#DC2626", "#64748B", "#7C3AED", "#059669"];

    private const string FontFamily = "Inter, 'IBM Plex Sans Arabic', sans-serif";
    private const int MaxCategories = 400;

    public static string Build(string type, IReadOnlyList<ResultSeries> series, double width = 640, double height = 300)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (series.Count == 0 || series[0].Categories.Count == 0)
        {
            return Empty(width, height);
        }

        return type switch
        {
            "pie" => Pie(series[0], width, height),
            "line" => Cartesian(series, width, height, line: true),
            _ => Cartesian(series, width, height, line: false),
        };
    }

    private static string Empty(double width, double height)
        => $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(width)}\" height=\"{F(height)}\" viewBox=\"0 0 {F(width)} {F(height)}\"><text x=\"{F(width / 2)}\" y=\"{F(height / 2)}\" text-anchor=\"middle\" font-family=\"{FontFamily}\" font-size=\"12\" fill=\"#64748B\">No data</text></svg>";

    private static string Cartesian(IReadOnlyList<ResultSeries> input, double width, double height, bool line)
    {
        var series = input.Select(s => new ResultSeries(s.Name, s.Categories.Take(MaxCategories).ToList(), s.Values.Take(MaxCategories).ToList())).ToList();
        var categories = series[0].Categories;
        var max = Math.Max(1e-9, series.SelectMany(s => s.Values).DefaultIfEmpty(0).Max());
        var min = Math.Min(0, series.SelectMany(s => s.Values).DefaultIfEmpty(0).Min());
        var (niceMin, niceMax, step) = NiceScale(min, max);
        var legendHeight = series.Count > 1 ? 18 : 0;
        var left = 56d;
        var right = 12d;
        var top = 14d;
        var bottom = 40d + legendHeight;
        var plotWidth = width - left - right;
        var plotHeight = height - top - bottom;
        double Y(double v) => top + plotHeight - (v - niceMin) / (niceMax - niceMin) * plotHeight;

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(width)}\" height=\"{F(height)}\" viewBox=\"0 0 {F(width)} {F(height)}\" font-family=\"{FontFamily}\">");

        // Grid and y labels.
        for (var v = niceMin; v <= niceMax + step / 2; v += step)
        {
            var y = Y(v);
            svg.Append(CultureInfo.InvariantCulture, $"<line x1=\"{F(left)}\" y1=\"{F(y)}\" x2=\"{F(width - right)}\" y2=\"{F(y)}\" stroke=\"#E2E8F0\" stroke-width=\"1\"/>");
            svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{F(left - 6)}\" y=\"{F(y + 4)}\" text-anchor=\"end\" font-size=\"10\" fill=\"#64748B\">{Esc(Compact(v))}</text>");
        }

        var slot = plotWidth / categories.Count;
        // x labels: skip some when crowded, rotate when long.
        var every = Math.Max(1, (int)Math.Ceiling(categories.Count / (plotWidth / 64)));
        for (var i = 0; i < categories.Count; i++)
        {
            if (i % every != 0)
            {
                continue;
            }

            var x = left + slot * (i + 0.5);
            var label = categories[i].Length > 16 ? categories[i][..15] + "…" : categories[i];
            svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{F(x)}\" y=\"{F(top + plotHeight + 14)}\" text-anchor=\"middle\" font-size=\"10\" fill=\"#64748B\">{Esc(label)}</text>");
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

        if (series.Count > 1)
        {
            var x = left;
            var y = height - 8;
            for (var s = 0; s < series.Count; s++)
            {
                var color = Palette[s % Palette.Length];
                svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{F(x)}\" y=\"{F(y - 9)}\" width=\"10\" height=\"10\" rx=\"2\" fill=\"{color}\"/>");
                svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{F(x + 14)}\" y=\"{F(y)}\" font-size=\"10\" fill=\"#334155\">{Esc(series[s].Name)}</text>");
                x += 20 + Math.Min(160, series[s].Name.Length * 6.2);
            }
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static string Pie(ResultSeries series, double width, double height)
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
                svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{F(height + 22)}\" y=\"{F(legendY)}\" font-size=\"10\" fill=\"#334155\">{Esc(series.Categories[i])} · {Esc(Compact(series.Values[i]))} ({share.ToString("P1", CultureInfo.InvariantCulture)})</text>");
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

    /// <summary>1.2M, 340K, 12.5 — compact axis labels.</summary>
    public static string Compact(double value)
    {
        var abs = Math.Abs(value);
        return abs switch
        {
            >= 1_000_000 => (value / 1_000_000).ToString("0.#", CultureInfo.InvariantCulture) + "M",
            >= 10_000 => (value / 1_000).ToString("0.#", CultureInfo.InvariantCulture) + "K",
            >= 100 => value.ToString("#,0", CultureInfo.InvariantCulture),
            _ => value.ToString("0.##", CultureInfo.InvariantCulture),
        };
    }

    private static string F(double value) => Math.Round(value, 2).ToString(CultureInfo.InvariantCulture);

    private static string Esc(string value) => value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
}
