using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using WabiInsight.Core.Ai;

namespace WabiInsight.Infrastructure.Reports;

/// <summary>Server-side chart rasterization (specification §20): the SVG the PDF uses, drawn to PNG through QuestPDF's Skia backend for DOCX and XLSX.</summary>
public static class ChartImageRenderer
{
    public const int Width = 760;
    public const int Height = 340;

    public static byte[] RenderPng(string type, IReadOnlyList<ResultSeries> series, int width = Width, int height = Height)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var svg = SvgChartBuilder.Build(type, series, width, height);
        var document = Document.Create(container => container.Page(page =>
        {
            page.Size(width, height);
            page.Margin(0);
            page.Content().Svg(svg);
        }));
        return document.GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 144 }).First();
    }
}
