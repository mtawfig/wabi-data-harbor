using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Entities.Analysis;
using WabiInsight.Infrastructure.Reports;

namespace WabiInsight.UnitTests.Ai;

/// <summary>DOCX and XLSX exports (specification §20): valid Office files, RTL for Arabic, charts rasterized, rows typed; sample files are written as fixtures for review.</summary>
public sealed class ExportTests
{
    private static readonly string FixtureDirectory = FindFixtureDirectory();

    private static AnalysisPanel Panel(string title, string type, IReadOnlyList<ResultColumn> columns, List<IReadOnlyList<object?>> rows, string? x, params string[] y)
    {
        var visualization = new PlanVisualization { Type = type, X = x, Y = [.. y] };
        var (kpis, series) = ResultCalculator.Calculate(columns, rows, visualization);
        return new AnalysisPanel(title, new QueryPlan { Title = title, Objective = title, Visualization = visualization }, new AnalysisResult(columns, rows, rows.Count, false, kpis, series, 1));
    }

    private static AskResponse Answer(bool arabic)
    {
        var month = new[] { new ResultColumn("month", "date", false, true), new ResultColumn("direction", "nvarchar", false, false), new ResultColumn("flights", "int", true, false) };
        var monthRows = Enumerable.Range(0, 12).SelectMany(m => new[] { arabic ? "وصول" : "Arrival", arabic ? "مغادرة" : "Departure" }.Select(d => (IReadOnlyList<object?>)new object?[] { new DateTime(2025, m + 1, 1), d, 5000 + m * 100 })).ToList();
        var airline = new[] { new ResultColumn("airline", "nvarchar", false, false), new ResultColumn("passengers", "int", true, false), new ResultColumn("avg_delay", "decimal", true, false) };
        var airlineRows = new List<IReadOnlyList<object?>> { new object?[] { arabic ? "العربية للطيران" : "Air Arabia", 14_000_000, 10.4m }, new object?[] { "IndiGo", 900_000, 9.1m }, new object?[] { arabic ? "الخطوط الباكستانية" : "PIA", 400_000, 12.7m } };
        var kpi = new[] { new ResultColumn("movements", "int", true, false) };
        var panels = new List<AnalysisPanel>
        {
            Panel(arabic ? "إجمالي الحركات" : "Total movements", "kpi", kpi, [new object?[] { 131516 }], null, "movements"),
            Panel(arabic ? "الحركات حسب الشهر" : "Movements by month", "line", month, monthRows, "month", "flights"),
            Panel(arabic ? "أكبر شركات الطيران" : "Top airlines", "table", airline, airlineRows, null),
        };
        var interpretation = new Interpretation(arabic ? "سجّل مطار الشارقة 131,516 حركة في 2025.\n\nذروة الحركة في أغسطس." : "Sharjah recorded 131,516 movements in 2025.\n\nAugust was the peak.", [arabic ? "131,516 حركة" : "131,516 movements", arabic ? "أغسطس هو الشهر الأعلى" : "August is the peak month"], new PlanUsage("Anthropic", "claude-opus-5-5", 100, 50, 0, 0.01m, 10));
        return new AskResponse(Guid.NewGuid(), Guid.NewGuid(), 1, arabic ? "حركة مطار الشارقة في 2025" : "Sharjah Airport traffic in 2025", [arabic ? "الأرقام تشمل الوصول والمغادرة" : "Figures include arrivals and departures"], panels, interpretation, null, null, interpretation.Usage, 0.02m, 2500);
    }

    private static ExportRequest Request(bool arabic)
    {
        var answer = Answer(arabic);
        var language = arabic ? "ar" : "en";
        var content = AnswerReportBuilder.Build(answer, answer.Objective, arabic ? "كم عدد الحركات في 2025؟" : "How many movements in 2025?", language);
        var logo = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"40\"><rect width=\"200\" height=\"40\" fill=\"#0B1F3A\"/></svg>");
        return new ExportRequest(answer, content, language, arabic ? "أكمي للخدمات اللوجستية" : "ACME Logistics", arabic ? "عمليات مطار الشارقة 2025" : "Sharjah Airport Operations 2025", arabic ? "كم عدد الحركات في 2025؟" : "How many movements in 2025?", new ReportBranding(logo, "image/svg+xml"), new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DocxExporter_ProducesAValidDocument_WithHeadingsTablesChartsAndDirection(bool arabic)
    {
        var request = Request(arabic);
        var bytes = new DocxExporter().Export(request);
        Assert.True(bytes.Length > 20_000, bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart!.Document!.Body!;
        var text = body.InnerText;
        Assert.Contains(request.Content.Title, text, StringComparison.Ordinal);
        Assert.Contains(arabic ? "الملخص التنفيذي" : "Executive summary", text, StringComparison.Ordinal);
        Assert.Contains("131,516", text, StringComparison.Ordinal);
        Assert.Contains("IndiGo", text, StringComparison.Ordinal);
        Assert.NotEmpty(document.MainDocumentPart!.ImageParts);
        Assert.Equal(2, body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Table>().Count());
        Assert.Equal(arabic, body.Descendants<DocumentFormat.OpenXml.Wordprocessing.BiDi>().Any());
        Assert.NotEmpty(document.MainDocumentPart!.HeaderParts);
        Assert.NotEmpty(document.MainDocumentPart.FooterParts);
        Assert.Contains("NUMPAGES", document.MainDocumentPart.FooterParts.First().Footer!.InnerText, StringComparison.Ordinal);

        // Business readers only: no methodology section, query counts or row counts (owner decision, P13.15).
        foreach (var technical in new[] { "Methodology", "المنهجية", "queries", "استعلام", " rows", "read-only", "للقراءة فقط" })
        {
            Assert.DoesNotContain(technical, text, StringComparison.OrdinalIgnoreCase);
        }

        WriteFixture($"sample-{(arabic ? "ar" : "en")}.docx", bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void XlsxExporter_ProducesAWorkbook_WithSummaryAndTypedPanelSheets(bool arabic)
    {
        var request = Request(arabic);
        var bytes = new XlsxExporter().Export(request);
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        Assert.Equal(4, workbook.Worksheets.Count);
        var summary = workbook.Worksheet(1);
        Assert.Equal(arabic, summary.RightToLeft);
        Assert.Equal(request.Content.Title, summary.Cell(1, 1).GetString());
        Assert.NotEmpty(summary.Pictures);
        var airlines = workbook.Worksheet(4);
        Assert.Equal("Airline", airlines.Cell(1, 1).GetString());
        Assert.Equal(14_000_000d, airlines.Cell(2, 2).GetDouble());
        Assert.Equal(10.4d, airlines.Cell(2, 3).GetDouble(), 3);
        var months = workbook.Worksheet(3);
        Assert.Equal(XLDataType.DateTime, months.Cell(2, 1).DataType);
        Assert.Equal(arabic, months.RightToLeft);
        WriteFixture($"sample-{(arabic ? "ar" : "en")}.xlsx", bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PdfExport_OfAStoredAnswer_UsesTheReportRenderer(bool arabic)
    {
        var request = Request(arabic);
        var pdf = new QuestPdfReportRenderer().RenderPdf(new ReportRenderRequest(request.Content, request.Answer.Panels, request.Branding, request.Language, request.OrganizationName, request.ApplicationName, request.OrganizationName, request.GeneratedAt));
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        WriteFixture($"sample-{(arabic ? "ar" : "en")}.pdf", pdf);
    }

    [Fact]
    public void SheetNames_AreExcelSafe_AndUnique()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.Equal("1. Movements by month", XlsxExporter.SheetName("Movements by month", 1, used));
        Assert.Equal("2. Movements by month", XlsxExporter.SheetName("Movements by month", 2, used));
        var longName = XlsxExporter.SheetName("A very long title that certainly exceeds the Excel limit [1]: *?", 3, used);
        Assert.True(longName.Length <= 31);
        Assert.DoesNotContain("[", longName, StringComparison.Ordinal);
        Assert.Equal("4. Panel 4", XlsxExporter.SheetName("[]", 4, used));
        var png = ChartImageRenderer.RenderPng("bar", [new ResultSeries("flights", ["a", "b"], [1, 2])]);
        Assert.Equal(0x89, png[0]);
        Assert.Equal((byte)'P', png[1]);
    }

    private static void WriteFixture(string name, byte[] bytes)
    {
        if (FixtureDirectory.Length > 0)
        {
            File.WriteAllBytes(Path.Combine(FixtureDirectory, name), bytes);
        }
    }

    /// <summary>tests/fixtures/exports when running from the repository; sample files are committed for human review of Arabic output.</summary>
    private static string FindFixtureDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures", "exports");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return string.Empty;
    }
}
