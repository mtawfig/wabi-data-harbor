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
/// P13.24: a reader must never take a shortened table for the whole list, and no headline figure may be left out.
/// The PDF is checked on its laid-out pages (text runs with their positions), the Word document on its XML.
/// </summary>
public sealed class ReportCompletenessTests
{
    private const string EnglishNote = "Showing the first 60 of 312. The Excel export has the full list.";
    private const string ArabicNote = "يعرض هذا الجدول أول 60 من أصل 312. القائمة الكاملة متوفرة في ملف Excel.";
    private static readonly CultureInfo Arabic = CultureInfo.GetCultureInfo("ar-AE");
    private static readonly string[] TechnicalWords = ["row", "query", "queries", "SQL", "fetch", "limit", "truncat", "صف", "صفوف", "استعلام", "سجل"];

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

    // The sentence itself.

    [Fact]
    public void ShortenedTableNote_GivesTheRealNumbers_InTheDocumentLanguage()
    {
        var long312 = TablePanel(312).Result;
        Assert.Equal(EnglishNote, QuestPdfReportRenderer.ShortenedTableNote(long312, QuestPdfReportRenderer.MaxTableRows, rtl: false, CultureInfo.InvariantCulture));
        Assert.Equal(ArabicNote, QuestPdfReportRenderer.ShortenedTableNote(long312, QuestPdfReportRenderer.MaxTableRows, rtl: true, Arabic));
        Assert.Equal("Showing the first 100 of 312. The Excel export has the full list.", QuestPdfReportRenderer.ShortenedTableNote(long312, DocxExporter.MaxTableRows, rtl: false, CultureInfo.InvariantCulture));
        Assert.Equal("يعرض هذا الجدول أول 100 من أصل 312. القائمة الكاملة متوفرة في ملف Excel.", QuestPdfReportRenderer.ShortenedTableNote(long312, DocxExporter.MaxTableRows, rtl: true, Arabic));

        // Numbers are written the way the tables write them.
        Assert.Equal("Showing the first 60 of 2,500. The Excel export has the full list.", QuestPdfReportRenderer.ShortenedTableNote(TablePanel(2500).Result, 60, rtl: false, CultureInfo.InvariantCulture));
        Assert.Equal("2,500", QuestPdfReportRenderer.FormatValue(2500, isNumeric: true, Arabic));
        Assert.Contains("من أصل 2,500.", QuestPdfReportRenderer.ShortenedTableNote(TablePanel(2500).Result, 60, rtl: true, Arabic), StringComparison.Ordinal);
    }

    [Fact]
    public void ShortenedTableNote_StaysTrue_WhenTheResultItselfStoppedAtItsSizeLimit()
    {
        var capped = TablePanel(1000, truncated: true).Result;

        // More exists than was counted, and the Excel file holds what was counted, not "the full list".
        Assert.Equal("Showing the first 60 of more than 1,000. The Excel export has the first 1,000.", QuestPdfReportRenderer.ShortenedTableNote(capped, 60, rtl: false, CultureInfo.InvariantCulture));
        Assert.Equal("يعرض هذا الجدول أول 60 من أصل أكثر من 1,000. ملف Excel يتضمن أول 1,000.", QuestPdfReportRenderer.ShortenedTableNote(capped, 60, rtl: true, Arabic));
        Assert.Equal("Showing the first 100 of more than 1,000. The Excel export has the first 1,000.", QuestPdfReportRenderer.ShortenedTableNote(capped, 100, rtl: false, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(59)]
    [InlineData(60)]
    public void ShortenedTableNote_IsAbsent_WhenTheTableShowsEverything(int rows)
    {
        Assert.Null(QuestPdfReportRenderer.ShortenedTableNote(TablePanel(rows).Result, 60, rtl: false, CultureInfo.InvariantCulture));
        Assert.Null(QuestPdfReportRenderer.ShortenedTableNote(TablePanel(rows).Result, 60, rtl: true, Arabic));
        Assert.NotNull(QuestPdfReportRenderer.ShortenedTableNote(TablePanel(61).Result, 60, rtl: false, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ShortenedTableNote_UsesBusinessWordsOnly()
    {
        foreach (var result in new[] { TablePanel(312).Result, TablePanel(1000, truncated: true).Result })
        {
            foreach (var note in new[] { QuestPdfReportRenderer.ShortenedTableNote(result, 60, false, CultureInfo.InvariantCulture)!, QuestPdfReportRenderer.ShortenedTableNote(result, 60, true, Arabic)! })
            {
                Assert.All(TechnicalWords, word => Assert.DoesNotContain(word, note, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    // PDF.

    [Fact]
    public void Pdf_NotesAShortenedTable_UnderTheTable_InEnglish()
    {
        var pages = PdfPages(PdfRequest([TablePanel(312)], "en"));
        var runs = pages.SelectMany(p => p).ToList();

        // Sixty of the 312 are drawn, and the line under the table says so, starting at the left margin.
        Assert.Contains(runs, r => r.Text == "Branch 0060");
        Assert.DoesNotContain(runs, r => r.Text == "Branch 0061");
        var note = Assert.Single(runs, r => r.Text == EnglishNote);
        var lastRow = runs.Last(r => r.Text == "Branch 0060");
        Assert.Contains(pages, p => p.Contains(note) && p.Contains(lastRow));
        Assert.True(note.Y > lastRow.Y, "The note is under the table.");
        Assert.InRange(note.Left, 47, 50);
    }

    [Fact]
    public void Pdf_NotesAShortenedTable_UnderTheTable_InArabic_RightToLeft()
    {
        var pages = PdfPages(PdfRequest([TablePanel(312)], "ar"));
        var runs = pages.SelectMany(p => p).ToList();
        var start = Assert.Single(runs, r => r.Text.StartsWith("يعرض هذا الجدول", StringComparison.Ordinal));
        var line = runs.Where(r => Math.Abs(r.Y - start.Y) < 0.01 && pages.Single(p => p.Contains(start)).Contains(r)).ToList();

        // Read from the right, the runs of the line give the sentence, with its Latin word and numbers in place.
        var read = string.Concat(line.OrderByDescending(r => r.Right).Select(r => r.Text));
        Assert.Equal(WithoutSpaces(ArabicNote), WithoutSpaces(read));
        Assert.Contains(line, r => r.Text == "60");
        Assert.Contains(line, r => r.Text == "312");
        Assert.Contains(line, r => r.Text == "Excel");

        // Right-aligned: the sentence starts at the right margin (A4 is 595 points wide, the margins 48) and it is under the table.
        Assert.InRange(line.Max(r => r.Right), 535, 548);
        Assert.True(line.Min(r => r.Left) > 200);
        Assert.True(start.Y > runs.Last(r => r.Text == "Branch 0060").Y);
        Assert.DoesNotContain(runs, r => r.Text == "Branch 0061");
        Assert.DoesNotContain(runs, r => r.Text.Contains("Showing", StringComparison.Ordinal));
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

    [Fact]
    public void Pdf_SaysMoreThan_WhenTheResultItselfWasCutAtTheSource()
    {
        var runs = PdfPages(PdfRequest([TablePanel(1000, truncated: true)], "en")).SelectMany(p => p).ToList();

        Assert.Single(runs, r => r.Text == "Showing the first 60 of more than 1,000. The Excel export has the first 1,000.");
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
    [InlineData("en", EnglishWordNote)]
    [InlineData("ar", ArabicWordNote)]
    public void Docx_NotesAShortenedTable_RightUnderTheTable(string language, string expected)
    {
        var body = DocxBody(ExportRequest([TablePanel(312)], language));
        var table = Assert.Single(body.Elements<Word.Table>());

        // The header and the first hundred of the 312.
        Assert.Equal(DocxExporter.MaxTableRows + 1, table.Elements<Word.TableRow>().Count());
        Assert.Contains("Branch 0100", table.InnerText, StringComparison.Ordinal);
        Assert.DoesNotContain("Branch 0101", table.InnerText, StringComparison.Ordinal);

        var note = Assert.IsType<Word.Paragraph>(table.NextSibling());
        Assert.Equal(expected, note.InnerText);
        Assert.Equal("TableNote", note.ParagraphProperties!.ParagraphStyleId!.Val!.Value);
        Assert.Equal(language == "ar", note.ParagraphProperties.GetFirstChild<Word.BiDi>() is not null);
        Assert.Equal(language == "ar", note.Descendants<Word.RightToLeftText>().Any());
        Assert.Single(body.Elements<Word.Paragraph>(), p => p.InnerText == expected);
        Assert.All(TechnicalWords, word => Assert.DoesNotContain(word, note.InnerText, StringComparison.OrdinalIgnoreCase));
    }

    private const string EnglishWordNote = "Showing the first 100 of 312. The Excel export has the full list.";
    private const string ArabicWordNote = "يعرض هذا الجدول أول 100 من أصل 312. القائمة الكاملة متوفرة في ملف Excel.";

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

    [Fact]
    public void Docx_SaysMoreThan_WhenTheResultItselfWasCutAtTheSource()
    {
        var body = DocxBody(ExportRequest([TablePanel(1000, truncated: true)], "en"));

        Assert.Equal("Showing the first 100 of more than 1,000. The Excel export has the first 1,000.", body.Elements<Word.Table>().Single().NextSibling()!.InnerText);
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

    // The writer is told the truth about what the documents contain.

    [Fact]
    public async Task ReportComposer_TellsTheWriter_ThatLongTablesAreShortenedInTheDocuments()
    {
        var provider = new CapturingProvider();
        var composer = new ReportComposer(provider);

        await composer.ComposeAsync(Guid.NewGuid(), "Branches?", "en", new AnalysisPlan { Objective = "Branches", Deliverables = ["report"] }, [TablePanel(312)], wantsEmail: false, "ACME", "Branches", CancellationToken.None);

        var prompt = Assert.Single(provider.Requests).UserContent;
        Assert.DoesNotContain("still draws every row", prompt, StringComparison.Ordinal);
        Assert.Contains("in the PDF and Word documents a long table is shortened too, with a note under it, and the Excel export has every row", prompt, StringComparison.Ordinal);
        Assert.Contains("a panel is complete when rowsShown equals rowCount", prompt, StringComparison.Ordinal);
        Assert.Contains("describe the listed rows as a sample and do not claim the rest is missing", prompt, StringComparison.Ordinal);
        Assert.Contains("\"rowsShown\":60,\"rowCount\":312,\"truncated\":false", prompt, StringComparison.Ordinal);
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

    private static string WithoutSpaces(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
}
