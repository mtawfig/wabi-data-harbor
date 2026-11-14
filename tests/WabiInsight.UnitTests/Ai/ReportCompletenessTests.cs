using System.Globalization;
using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using QuestPDF.Fluent;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Entities.Ai;
using WabiInsight.Core.Entities.Analysis;
using WabiInsight.Infrastructure.Reports;
using Word = DocumentFormat.OpenXml.Wordprocessing;

namespace WabiInsight.UnitTests.Ai;

/// <summary>
/// P13.24 and P13.49: a long table takes a fixed number of rows in a document and the document says nothing about it
/// (the owner's decision: no reader is told that a list is partial); no headline figure may be left out.
/// The PDF is checked on its laid-out pages (text runs with their positions), the Word document on its XML.
/// </summary>
public sealed class ReportCompletenessTests
{
    private static readonly CultureInfo Arabic = CultureInfo.GetCultureInfo("ar-AE");
    private static readonly string[] PartialWords = ["Showing", "the first", "Excel", "more than", "partial", "يعرض هذا الجدول", "أول 60", "أول 100", "من أصل"];

    private static AnalysisPanel TablePanel(int rowCount, bool truncated = false, string title = "Branches")
    {
        var columns = new[] { new ResultColumn("branch", "nvarchar", false, false), new ResultColumn("amount", "int", true, false) };
        var rows = Enumerable.Range(1, rowCount).Select(i => (IReadOnlyList<object?>)new object?[] { $"Branch {i:0000}", i * 10 }).ToList();
        return new AnalysisPanel(title, new QueryPlan { Title = title, Objective = title, Visualization = new PlanVisualization { Type = "table" } }, new AnalysisResult(columns, rows, rows.Count, truncated, [], [], 1));
    }

    /// <summary>Headline figures 1,111 · 2,222 · … with the titles "Figure 1" · "Figure 2" · …</summary>
    private static List<AnalysisPanel> KpiPanels(int count) => Enumerable.Range(1, count).Select(i =>
    {
        var title = $"Figure {i}";
        var visualization = new PlanVisualization { Type = "kpi", Y = ["total"] };
        return new AnalysisPanel(title, new QueryPlan { Title = title, Objective = title, Visualization = visualization }, new AnalysisResult([new ResultColumn("total", "int", true, false)], [new object?[] { i * 1111 }], 1, false, [], [], 1));
    }).ToList();

    private static ReportContent Content(int panels) => new() { Title = "Branch study", Sections = [new ReportSection { Title = "Overview", Body = "Body.", Panels = [.. Enumerable.Range(0, panels)] }] };

    private static ReportRenderRequest PdfRequest(List<AnalysisPanel> panels, string language)
        => new(Content(panels.Count), panels, new ReportBranding(null, null), language, "ACME", "Branches", "ACME", new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero));

    private static ExportRequest ExportRequest(List<AnalysisPanel> panels, string language)
    {
        var usage = new PlanUsage("Test", "test-model", 1, 1, 0, 0m, 1);
        var answer = new AskResponse(Guid.NewGuid(), Guid.NewGuid(), 1, "Branch study", [], panels, new Interpretation("Summary.", [], usage), null, null, usage, 0m, 1);
        return new ExportRequest(answer, Content(panels.Count), language, "ACME", "Branches", "Question?", new ReportBranding(null, null), new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero));
    }

    // PDF.

    [Theory]
    [InlineData("en", 312, false)]
    [InlineData("ar", 312, false)]
    [InlineData("en", 1000, true)]
    [InlineData("ar", 1000, true)]
    public void Pdf_NeverSaysThatATableIsShortened(string language, int rows, bool cutAtTheSource)
    {
        var text = string.Join(" ", PdfPages(PdfRequest([TablePanel(rows, truncated: cutAtTheSource)], language)).SelectMany(p => p).Select(r => r.Text));

        // Sixty rows are drawn and nothing under the table, or anywhere else, speaks of the rest.
        Assert.Contains("Branch 0060", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Branch 0061", text, StringComparison.Ordinal);
        Assert.All(PartialWords, word => Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("en", 60)]
    [InlineData("en", 12)]
    [InlineData("ar", 60)]
    public void Pdf_AddsNoNote_WhenTheWholeTableIsDrawn(string language, int rows)
    {
        var text = string.Join(" ", PdfPages(PdfRequest([TablePanel(rows)], language)).SelectMany(p => p).Select(r => r.Text));

        Assert.Contains($"Branch {rows:0000}", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Showing", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Excel", text, StringComparison.Ordinal);
        Assert.DoesNotContain("الجدول", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public void Pdf_DrawsAllSevenKpisOfASection_OnTwoRowsOfCards(string language)
    {
        var runs = PdfPages(PdfRequest(KpiPanels(7), language)).Single();
        var values = Enumerable.Range(1, 7).Select(i => Assert.Single(runs, r => r.Text == (i * 1111).ToString("#,0", CultureInfo.InvariantCulture))).ToList();
        Assert.All(Enumerable.Range(1, 7), i => Assert.Single(runs, r => r.Text == $"Figure {i}"));

        // Four cards, then three under them with the same width (the same positions as the cards above).
        Assert.All(values.Take(4), v => Assert.Equal(values[0].Y, v.Y, 1));
        Assert.All(values.Skip(4), v => Assert.Equal(values[4].Y, v.Y, 1));
        Assert.True(values[4].Y > values[0].Y + 30);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(values[i].Left, values[i + 4].Left, 1);
        }

        // English fills from the left, Arabic from the right.
        Assert.Equal(language == "ar", values[0].Left > values[3].Left);
    }

    [Fact]
    public void KpiRows_KeepEveryFigure_AtMostFiveSideBySide()
    {
        Assert.Empty(QuestPdfReportRenderer.KpiRows(Array.Empty<int>()));
        Assert.Equal([[1]], QuestPdfReportRenderer.KpiRows([1]));
        Assert.Equal([[1, 2, 3, 4, 5]], QuestPdfReportRenderer.KpiRows([1, 2, 3, 4, 5]));
        Assert.Equal([[1, 2, 3], [4, 5, 6]], QuestPdfReportRenderer.KpiRows([1, 2, 3, 4, 5, 6]));
        Assert.Equal([[1, 2, 3, 4], [5, 6, 7]], QuestPdfReportRenderer.KpiRows([1, 2, 3, 4, 5, 6, 7]));
        Assert.Equal([4, 4, 3], QuestPdfReportRenderer.KpiRows(Enumerable.Range(1, 11).ToList()).Select(r => r.Count));
        for (var count = 1; count <= 40; count++)
        {
            var rows = QuestPdfReportRenderer.KpiRows(Enumerable.Range(1, count).ToList());
            Assert.Equal(Enumerable.Range(1, count), rows.SelectMany(r => r));
            Assert.All(rows, r => Assert.InRange(r.Count, 1, QuestPdfReportRenderer.MaxKpisPerRow));
        }
    }

    // Word.

    [Theory]
    [InlineData("en", 312, false)]
    [InlineData("ar", 312, false)]
    [InlineData("en", 1000, true)]
    [InlineData("ar", 1000, true)]
    public void Docx_NeverSaysThatATableIsShortened(string language, int rows, bool cutAtTheSource)
    {
        var body = DocxBody(ExportRequest([TablePanel(rows, truncated: cutAtTheSource)], language));
        var table = Assert.Single(body.Elements<Word.Table>());

        // The header and the first hundred rows, and no line about the rest.
        Assert.Equal(DocxExporter.MaxTableRows + 1, table.Elements<Word.TableRow>().Count());
        Assert.Contains("Branch 0100", table.InnerText, StringComparison.Ordinal);
        Assert.DoesNotContain("Branch 0101", table.InnerText, StringComparison.Ordinal);
        Assert.DoesNotContain(body.Elements<Word.Paragraph>(), p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "TableNote");
        Assert.All(PartialWords, word => Assert.DoesNotContain(word, body.InnerText, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("en", 100)]
    [InlineData("en", 8)]
    [InlineData("ar", 100)]
    public void Docx_AddsNoNote_WhenTheWholeTableIsThere(string language, int rows)
    {
        var body = DocxBody(ExportRequest([TablePanel(rows)], language));
        var table = Assert.Single(body.Elements<Word.Table>());

        Assert.Equal(rows + 1, table.Elements<Word.TableRow>().Count());
        Assert.DoesNotContain(body.Elements<Word.Paragraph>(), p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "TableNote");
        Assert.DoesNotContain("Showing", body.InnerText, StringComparison.Ordinal);
        Assert.DoesNotContain("Excel", body.InnerText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public void Docx_DrawsAllSevenKpisOfASection_OnTwoRowsOfFigures(string language)
    {
        var body = DocxBody(ExportRequest(KpiPanels(7), language));
        var table = Assert.Single(body.Elements<Word.Table>());
        var rows = table.Elements<Word.TableRow>().ToList();

        // Figures and their names alternate: four and four, then three and three completed with an empty cell.
        Assert.Equal(4, rows.Count);
        Assert.All(rows, r => Assert.Equal(4, r.Elements<Word.TableCell>().Count()));
        Assert.Equal(["1,111", "2,222", "3,333", "4,444"], rows[0].Elements<Word.TableCell>().Select(c => c.InnerText));
        Assert.Equal(["Figure 1", "Figure 2", "Figure 3", "Figure 4"], rows[1].Elements<Word.TableCell>().Select(c => c.InnerText));
        Assert.Equal(["5,555", "6,666", "7,777", ""], rows[2].Elements<Word.TableCell>().Select(c => c.InnerText));
        Assert.Equal(["Figure 5", "Figure 6", "Figure 7", ""], rows[3].Elements<Word.TableCell>().Select(c => c.InnerText));

        // Up to five figures keep the single row they had.
        var five = DocxBody(ExportRequest(KpiPanels(5), language)).Elements<Word.Table>().Single().Elements<Word.TableRow>().ToList();
        Assert.Equal(2, five.Count);
        Assert.All(five, r => Assert.Equal(5, r.Elements<Word.TableCell>().Count()));
    }

    // Excel is unchanged and is what the note points to.

    [Fact]
    public void Xlsx_StillHasEveryRow_OfATableTheDocumentsShorten()
    {
        var bytes = new XlsxExporter().Export(ExportRequest([TablePanel(312)], "en"));
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.Last();

        Assert.Equal("Branch 0001", sheet.Cell(2, 1).GetString());
        Assert.Equal("Branch 0312", sheet.Cell(313, 1).GetString());
        Assert.Equal(3120d, sheet.Cell(313, 2).GetDouble());
        Assert.True(sheet.Cell(314, 1).IsEmpty());
    }

    // What "cut" means.

    [Fact]
    public void ARequestedNumberOfRows_IsAComplete_Answer_AndAListCutAtTheSizeLimit_KeepsNoTotals()
    {
        var kpis = new List<ResultKpi> { new("Amount", "sum", 500) };
        var cut = TablePanel(5, truncated: true).Result with { Kpis = kpis };

        // "Top five": the five rows are the answer, with their totals.
        var topFive = cut.For(new QueryPlan { Limit = 5 });
        Assert.False(topFive.Truncated);
        Assert.Equal(kpis, topFive.Kpis);

        // No limit of its own, or the maximum: the list stopped at the size limit, and a total of the rows fetched is not the total of the list.
        foreach (var plan in new[] { new QueryPlan(), new QueryPlan { Limit = 1000 } })
        {
            var capped = cut.For(plan);
            Assert.True(capped.Truncated);
            Assert.Empty(capped.Kpis);
        }

        // A result that was not cut is left as it is.
        var whole = TablePanel(5).Result with { Kpis = kpis };
        Assert.Same(whole, whole.For(new QueryPlan()));
    }

    // The writer is never asked to announce that a list is partial.

    [Fact]
    public async Task ReportComposer_TellsTheWriter_NeverToSayThatAListIsPartial()
    {
        var provider = new CapturingProvider();
        var composer = new ReportComposer(provider);

        await composer.ComposeAsync(Guid.NewGuid(), "Branches?", "en", new AnalysisPlan { Objective = "Branches", Deliverables = ["report"] }, [TablePanel(312), TablePanel(1000, truncated: true, title: "Customers")], wantsEmail: false, "ACME", "Branches", CancellationToken.None);

        var prompt = Assert.Single(provider.Requests).UserContent;
        Assert.Contains("never say that a list or table is partial, limited, shortened, a sample or the first rows, or that more rows exist", prompt, StringComparison.Ordinal);
        Assert.Contains("never conclude that something is absent", prompt, StringComparison.Ordinal);
        Assert.Contains("do not state a total, count, average, ranking, highest or lowest for it unless it is in totals", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("with a note under it", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("describe the listed rows as a sample", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("rowsShown", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("truncated", prompt, StringComparison.Ordinal);
        // The count is the true number of rows, and is left out when the result stopped at its size limit.
        Assert.Contains("\"rowCount\":312,\"complete\":false", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"rowCount\":1000", prompt, StringComparison.Ordinal);
    }

    private sealed class CapturingProvider : IAiProvider, IAiProviderResolver
    {
        public List<AiRequest> Requests { get; } = [];

        public AiProviderType ProviderType => AiProviderType.OpenAiCompatible;

        public Task<AiCompletion> CompleteAsync(AiProviderDescriptor descriptor, AiRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new AiCompletion("{\"title\":\"Branch study\",\"sections\":[]}", new AiUsage(1, 1, 0), "test-model", "stop"));
        }

        public Task<ResolvedAiProvider> ResolveForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
            => Task.FromResult(new ResolvedAiProvider(new AiProviderDescriptor(Guid.NewGuid(), ProviderType, null, "test-model", "key", 4096, null, 0m, 0m, 0m), this, AiProviderScope.Platform, "Test"));
    }

    // Helpers.

    private static Word.Body DocxBody(ExportRequest request)
    {
        using var stream = new MemoryStream(new DocxExporter().Export(request));
        using var document = WordprocessingDocument.Open(stream, false);
        return (Word.Body)document.MainDocumentPart!.Document!.Body!.CloneNode(true);
    }

    /// <summary>One run of text as laid out on a page: where it starts and ends (points from the left edge) and its baseline.</summary>
    private sealed record TextRun(string Text, double Left, double Right, double Y);

    /// <summary>
    /// The text runs of every page of the laid-out document. Arabic runs come out in display order with contextual
    /// letter forms; they are turned back into reading order and plain letters so they can be compared with the source.
    /// </summary>
    private static List<List<TextRun>> PdfPages(ReportRenderRequest request)
    {
        _ = new QuestPdfReportRenderer();
        var pages = new List<List<TextRun>>();
        foreach (var svg in QuestPdfReportRenderer.Compose(request).GenerateSvg())
        {
            var runs = new List<TextRun>();
            foreach (var element in XDocument.Parse(svg).Descendants().Where(e => e.Name.LocalName == "text"))
            {
                var raw = element.Value.Trim();
                if (raw.Length == 0)
                {
                    continue;
                }

                var (x, y) = Origin(element.Attribute("transform")?.Value);
                var offsets = (element.Attribute("x")?.Value ?? "0").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToList();
                var text = raw.Any(IsArabic) ? new string(raw.Reverse().ToArray()).Normalize(NormalizationForm.FormKC) : raw;
                runs.Add(new TextRun(text, x + offsets.Min(), x + offsets.Max(), y));
            }

            pages.Add(runs);
        }

        return pages;
    }

    /// <summary>The origin of an element from its transform: "translate(x y)" or "matrix(a b c d x y)".</summary>
    private static (double X, double Y) Origin(string? transform)
    {
        if (string.IsNullOrEmpty(transform))
        {
            return (0, 0);
        }

        var open = transform.IndexOf('(', StringComparison.Ordinal);
        var close = transform.IndexOf(')', StringComparison.Ordinal);
        var numbers = transform[(open + 1)..close].Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToList();
        return transform.StartsWith("matrix", StringComparison.Ordinal) ? (numbers[4], numbers[5]) : (numbers[0], numbers.Count > 1 ? numbers[1] : 0);
    }

    private static bool IsArabic(char c) => c is (>= '؀' and <= 'ۿ') or (>= 'ﭐ' and <= '﷿') or (>= 'ﹰ' and <= 'ﻼ');

}
