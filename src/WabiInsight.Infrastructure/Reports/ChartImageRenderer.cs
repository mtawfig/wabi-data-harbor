using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using WabiInsight.Core.Ai;

namespace WabiInsight.Infrastructure.Reports;

/// <summary>A chart drawn to PNG and its size in pixels at 1x (the image itself is drawn at 2x for sharpness).</summary>
public sealed record ChartImage(byte[] Png, int Width, int Height);

/// <summary>Server-side chart rasterization (specification §20): the SVG the PDF uses, drawn to PNG through QuestPDF's Skia backend for DOCX and XLSX.</summary>
public static class ChartImageRenderer
{
    public const int Width = 760;
    public const int Height = 340;

    public static byte[] RenderPng(string type, IReadOnlyList<ResultSeries> series, int width = Width, int height = Height)
        => RenderImage(type, series, width, height).Png;

    /// <summary>Draws the chart; the image is taller than <paramref name="height"/> when its category labels need the room.</summary>
    public static ChartImage RenderImage(string type, IReadOnlyList<ResultSeries> series, int width = Width, int height = Height, CultureInfo? culture = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var chart = SvgChartBuilder.Render(type, series, width, height, culture);
        var finalHeight = (int)Math.Ceiling(chart.Height);
        var document = Document.Create(container => container.Page(page =>
        {
            page.Size(width, finalHeight);
            page.Margin(0);
            page.Content().Svg(chart.Svg);
        }));
        return new ChartImage(document.GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 144 }).First(), width, finalHeight);
    }
}
