using System.Globalization;
using System.Text;
using WabiInsight.Core.Ai;
using WabiInsight.Infrastructure.Reports;

namespace WabiInsight.UnitTests.Ai;

public sealed class ReportTests
{
    private static AnalysisPanel Panel(string title, string type, IReadOnlyList<ResultColumn> columns, List<IReadOnlyList<object?>> rows, string? x, params string[] y)
    {
        var visualization = new PlanVisualization { Type = type, X = x, Y = [.. y] };
        var (kpis, series) = ResultCalculator.Calculate(columns, rows, visualization);
        return new AnalysisPanel(title, new QueryPlan { Title = title, Objective = title, Visualization = visualization }, new AnalysisResult(columns, rows, rows.Count, false, kpis, series, 1));
    }

    private static List<AnalysisPanel> SamplePanels()
    {
        var month = new[] { new ResultColumn("month", "date", false, true), new ResultColumn("direction", "nvarchar", false, false), new ResultColumn("flights", "int", true, false) };
        var monthRows = Enumerable.Range(0, 12).SelectMany(m => new[] { "Arrival", "Departure" }.Select(d => (IReadOnlyList<object?>)new object?[] { new DateTime(2025, m + 1, 1), d, 5000 + m * 100 + (d == "Arrival" ? 0 : 7) })).ToList();
        var day = new[] { new ResultColumn("day", "nvarchar", false, false), new ResultColumn("flights", "int", true, false) };
        var dayRows = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }.Select((d, i) => (IReadOnlyList<object?>)new object?[] { d, 17000 + i * 300 }).ToList();
        var status = new[] { new ResultColumn("status", "nvarchar", false, false), new ResultColumn("flights", "int", true, false) };
        var statusRows = new List<IReadOnlyList<object?>> { new object?[] { "On Time", 90000 }, new object?[] { "Delayed", 40000 }, new object?[] { "Cancelled", 1448 } };
        var kpi = new[] { new ResultColumn("movements", "int", true, false) };
        var table = new[] { new ResultColumn("airport_code", "nvarchar", false, false), new ResultColumn("passengers", "int", true, false), new ResultColumn("avg_delay", "decimal", true, false) };
        var tableRows = Enumerable.Range(0, 40).Select(i => (IReadOnlyList<object?>)new object?[] { $"AP{i:00}", 400000 - i * 1000, 10.25m }).ToList();
        return
        [
            Panel("Total movements", "kpi", kpi, [new object?[] { 131516 }], null, "movements"),
            Panel("Flights per month by direction", "line", month, monthRows, "month", "flights"),
            Panel("Flights by day of week", "bar", day, dayRows, "day", "flights"),
            Panel("Flights by status", "pie", status, statusRows, "status", "flights"),
            Panel("Top destinations", "table", table, tableRows, null),
        ];
    }

    [Fact]
    public void SvgChartBuilder_DrawsGroupedBars_Lines_AndPies_FromSeriesOnly()
    {
        var panels = SamplePanels();
        var line = SvgChartBuilder.Build("line", panels[1].Result.Series);
        Assert.StartsWith("<svg", line, StringComparison.Ordinal);
        Assert.Equal(2, line.Split("<polyline").Length - 1);
        Assert.Contains("Departure", line, StringComparison.Ordinal);
        var bar = SvgChartBuilder.Build("bar", panels[2].Result.Series);
        Assert.Equal(7, bar.Split("<rect").Length - 1);
        var pie = SvgChartBuilder.Build("pie", panels[3].Result.Series);
        Assert.Contains("<path", pie, StringComparison.Ordinal);
        Assert.Contains("On Time", pie, StringComparison.Ordinal);
        Assert.StartsWith("<svg", SvgChartBuilder.Build("bar", []), StringComparison.Ordinal);
        var hostile = SvgChartBuilder.Build("bar", [new ResultSeries("<script>", ["<b>&"], [1])]);
        Assert.DoesNotContain("<script>", hostile, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;&amp;", hostile, StringComparison.Ordinal);
        Assert.Equal("1.2M", SvgChartBuilder.Compact(1_234_567));
        Assert.Equal("12.3K", SvgChartBuilder.Compact(12_345));
    }

    [Fact]
    public void ReportComposer_Parse_SanitizesAndKeepsEveryPanelExactlyOnce()
    {
        var json = """
            {"title":"Sharjah <<study>>","subtitle":"2025","introduction":"First.\n\nSecond\u0007 paragraph.","sections":[{"title":"Overview","body":"Body","panels":[0,0,7]},{"title":"Trend","body":"More","panels":[1,0]}],
             "summary":"Sum","keyFindings":["a","", "b"],"recommendations":[{"title":"Do","detail":"It"}],"email":{"subject":"Hi","body":"Dear team,\n\nSee attached."}}
            """;
        var content = ReportComposer.Parse(json, panelCount: 3, wantsEmail: true);
        Assert.Equal("Sharjah «study»", content.Title);
        Assert.Equal("First.\n\nSecond paragraph.", content.Introduction);
        Assert.Equal([0], content.Sections[0].Panels);
        Assert.Equal([1, 2], content.Sections[1].Panels);
        Assert.Equal(["a", "b"], content.KeyFindings);
        Assert.Equal("Dear team,\n\nSee attached.", content.Email!.Body);
        Assert.Null(ReportComposer.Parse(json, 3, wantsEmail: false).Email);
        var fallback = ReportComposer.Parse("not json at all", 1, false);
        Assert.Equal("Report", fallback.Title);
        Assert.Equal([0], Assert.Single(fallback.Sections).Panels);
    }

    [Fact]
    public void ReportComposer_Parse_RemovesMarkdownTheModelAddedAnyway()
    {
        var json = """
            {"title":"## **Cheque** study","subtitle":"","introduction":"","sections":[{"title":"Overview","body":"Returns rose.\n- **Insufficient funds:** 72% of returns\n* *Post-dated* cheques: 48","panels":[0]}],
             "summary":"Three points:\n- **Sharjah** leads\n- 2 * 3 = 6 stays","keyFindings":["**546** returned"],"recommendations":[{"title":"__Check__ dates","detail":"Use `intake` checks."}],
             "email":{"subject":"**Summary**","body":"1. **Total overrun:** AED 1,267,822.\n2. Second point."}}
            """;
        var content = ReportComposer.Parse(json, panelCount: 1, wantsEmail: true);
        Assert.Equal("Cheque study", content.Title);
        Assert.Equal("Returns rose.\n• Insufficient funds: 72% of returns\n• Post-dated cheques: 48", content.Sections[0].Body);
        Assert.Equal("Three points:\n• Sharjah leads\n• 2 * 3 = 6 stays", content.Summary);
        Assert.Equal(["546 returned"], content.KeyFindings);
        Assert.Equal("Check dates", content.Recommendations[0].Title);
        Assert.Equal("Use intake checks.", content.Recommendations[0].Detail);
        Assert.Equal("Summary", content.Email!.Subject);
        Assert.Equal("1. Total overrun: AED 1,267,822.\n2. Second point.", content.Email.Body);
    }

    [Fact]
    public void QuestPdfReportRenderer_ProducesAPdf_WithLogoChartsTablesAndArabic()
    {
        var content = new ReportContent
        {
            Title = "Sharjah Airport 2025 punctuality study",
            Subtitle = "Departures and arrivals, calendar year 2025",
            Introduction = "Purpose.\n\nScope and data.",
            Sections =
            [
                new ReportSection { Title = "Overview", Body = "Headline numbers.", Panels = [0] },
                new ReportSection { Title = "Seasonality", Body = "Monthly pattern.\n\nSee Figure 2.1.", Panels = [1, 2] },
                new ReportSection { Title = "Punctuality", Body = "Shares.", Panels = [3, 4] },
            ],
            Summary = "Summary paragraph.",
            KeyFindings = ["131,516 movements", "Friday busiest"],
            Recommendations = [new ReportRecommendation { Title = "Evening bank", Detail = "Rebalance." }],
        };
        var logo = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"40\"><rect width=\"200\" height=\"40\" fill=\"#0B1F3A\"/><text x=\"10\" y=\"26\" fill=\"#fff\" font-size=\"18\">Sharjah Airport</text></svg>");
        var renderer = new QuestPdfReportRenderer();
        var pdf = renderer.RenderPdf(new ReportRenderRequest(content, SamplePanels(), new ReportBranding(logo, "image/svg+xml"), "en", "ACME Logistics", "SHJ-OPS", "ACME Logistics", new DateTimeOffset(2026, 11, 6, 1, 0, 0, TimeSpan.Zero)));
        Assert.True(pdf.Length > 20_000, pdf.Length.ToString(CultureInfo.InvariantCulture));
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));

        var arabic = new ReportContent { Title = "دراسة الانضباط الزمني", Introduction = "مقدمة.", Sections = [new ReportSection { Title = "نظرة عامة", Body = "نص.", Panels = [0, 1, 2, 3, 4] }], Summary = "ملخص.", KeyFindings = ["نتيجة"] };
        var arabicPdf = renderer.RenderPdf(new ReportRenderRequest(arabic, SamplePanels(), new ReportBranding(null, null), "ar", "أكمي", "SHJ-OPS", "أكمي", DateTimeOffset.UtcNow));
        Assert.Equal("%PDF", Encoding.ASCII.GetString(arabicPdf, 0, 4));
        Assert.Equal("sharjah-airport-2025-punctuality-study-20261106.pdf", Infrastructure.Ai.AnalysisService.FileName(content.Title, new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero)));
        // An Arabic title keeps its words in the file name (P13.55); a title with nothing to keep falls back to "report".
        Assert.Equal("دراسة-انضباط-المواعيد-2025-20261106.pdf", Infrastructure.Ai.AnalysisService.FileName("دراسة انضباط المواعيد 2025", new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal("report-20261106.pdf", Infrastructure.Ai.AnalysisService.FileName("—  ؟", new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal("Monthly Traffic", QuestPdfReportRenderer.StripNumbering("2. 2. Monthly Traffic"));
        Assert.Equal("Overview", QuestPdfReportRenderer.StripNumbering("Overview"));
    }

    // P13.16: report layout. Figures and words never break, chart labels never overlap, figure references read naturally.

    [Fact]
    public void LayoutTable_SizesColumnsSoFiguresAndHeaderWordsStayWhole()
    {
        string[] headers = ["Branch", "Opening cash", "Cash in", "Cash out", "Cash difference", "Transactions", "Tellers", "Business days"];
        bool[] numeric = [false, true, true, true, true, true, true, true];
        IReadOnlyList<string>[] rows =
        [
            ["Deira (Dubai Main)", "83,887,000", "390,180,346.11", "290,383,847.84", "-100", "50,791", "7", "251"],
            ["Ras Al Khaimah", "92,171,000", "85,504,344.48", "60,702,140.19", "250", "15,287", "6", "251"],
        ];
        var layout = QuestPdfReportRenderer.LayoutTable(headers, numeric, rows);

        Assert.Equal(QuestPdfReportRenderer.TableWidth, layout.Widths.Sum(), 1);
        for (var i = 0; i < headers.Length; i++)
        {
            var inner = layout.Widths[i] - 2 * layout.PaddingX;
            Assert.True(inner >= TextMetrics.LongestWord(headers[i], layout.HeaderFontSize, bold: true), headers[i]);
            if (numeric[i])
            {
                Assert.True(inner >= rows.Max(r => TextMetrics.Width(r[i], layout.FontSize)), headers[i]);
            }
        }

        // A table too wide for the page keeps every figure whole by using smaller type, never below 6.5 pt.
        var many = Enumerable.Range(1, 14).Select(i => $"Amount {i}").ToList();
        var wide = QuestPdfReportRenderer.LayoutTable(many, many.Select(_ => true).ToList(), [many.Select(_ => "1,234,567,890.12").ToList()]);
        Assert.InRange(wide.FontSize, 6.5f, 8.9f);
        Assert.Equal(3f, wide.PaddingX);
        Assert.Empty(QuestPdfReportRenderer.LayoutTable([], [], []).Widths);
    }

    [Fact]
    public void SvgChartBuilder_WrapsOrAnglesLongCategoryLabels_InsteadOfOverlappingThem()
    {
        string[] branches = ["Sharjah", "Al Ain", "Business Bay", "Deira (Dubai Main)", "Abu Dhabi Main", "Ras Al Khaimah"];
        var twoLines = SvgChartBuilder.Render("bar", [new ResultSeries("cash_difference", branches, [-6390, -250, -150, -100, 50, 250])], 520, 240);
        Assert.Contains(">Deira (Dubai</text>", twoLines.Svg, StringComparison.Ordinal);
        Assert.Contains(">Main)</text>", twoLines.Svg, StringComparison.Ordinal);
        Assert.DoesNotContain("…", twoLines.Svg, StringComparison.Ordinal);
        Assert.DoesNotContain("rotate(", twoLines.Svg, StringComparison.Ordinal);

        // Forty tellers: labels are drawn at an angle and the chart grows to hold them; the plot keeps its height.
        var tellers = Enumerable.Range(1, 40).Select(i => $"Teller number {i:00} of the branch").ToList();
        var angled = SvgChartBuilder.Render("bar", [new ResultSeries("transactions", tellers, tellers.Select((_, i) => (double)(i + 1)).ToList())], 520, 240);
        Assert.Contains("rotate(-45", angled.Svg, StringComparison.Ordinal);
        Assert.True(angled.Height > 240, angled.Height.ToString(CultureInfo.InvariantCulture));
        Assert.Contains($"height=\"{angled.Height.ToString(CultureInfo.InvariantCulture)}\"", angled.Svg, StringComparison.Ordinal);

        // Short labels stay on one line, unchanged.
        var days = SvgChartBuilder.Render("bar", [new ResultSeries("flights", ["Mon", "Tue", "Wed"], [1, 2, 3])], 520, 240);
        Assert.Equal(240, days.Height);
        Assert.Contains(">Tue</text>", days.Svg, StringComparison.Ordinal);
    }

    [Fact]
    public void SvgChartBuilder_NamesSeriesInWords_MonthsByName_AndBillionsCompactly()
    {
        string[] months = ["2025-01-01", "2025-02-01", "2025-03-01"];
        var chart = SvgChartBuilder.Build("line", [new ResultSeries("cash_in", months, [1, 2, 3]), new ResultSeries("cash_out", months, [1, 1, 2])]);
        Assert.Contains(">Cash in</text>", chart, StringComparison.Ordinal);
        Assert.DoesNotContain("cash_in", chart, StringComparison.Ordinal);
        Assert.Contains(">Jan 2025</text>", chart, StringComparison.Ordinal);

        Assert.Equal(["Jan 2025", "Feb 2025"], SvgChartBuilder.CategoryLabels(["2025-01-01T00:00:00", "2025-02-01T00:00:00"], CultureInfo.InvariantCulture));
        Assert.Equal(["2024", "2025"], SvgChartBuilder.CategoryLabels(["2024-01-01", "2025-01-01"], CultureInfo.InvariantCulture));
        Assert.Equal(["2025-01-15", "2025-02-01"], SvgChartBuilder.CategoryLabels(["2025-01-15", "2025-02-01"], CultureInfo.InvariantCulture));
        Assert.Equal(["Deira", "Sharjah"], SvgChartBuilder.CategoryLabels(["Deira", "Sharjah"], CultureInfo.InvariantCulture));
        Assert.Equal(["يناير 2025", "فبراير 2025"], SvgChartBuilder.CategoryLabels(["2025-01-01", "2025-02-01"], CultureInfo.GetCultureInfo("ar-AE")));
        Assert.Equal("1.35B", SvgChartBuilder.Compact(1_354_955_706));
    }

    [Fact]
    public void ChartImageRenderer_SizesTheImage_ToTheChart()
    {
        var tellers = Enumerable.Range(1, 40).Select(i => $"Teller number {i:00} of the branch").ToList();
        var image = ChartImageRenderer.RenderImage("bar", [new ResultSeries("transactions", tellers, tellers.Select((_, i) => (double)i).ToList())]);
        Assert.Equal(ChartImageRenderer.Width, image.Width);
        Assert.True(image.Height > ChartImageRenderer.Height);
        Assert.Equal(0x89, image.Png[0]);
    }

    [Fact]
    public void ReportText_DropsFigureAndTableLabels_TheDocumentCaptionsItself()
    {
        var json = """
            {"title":"Cash","introduction":"Figure: Total cash in and Table: Cash by branch summarise the year.","sections":[{"title":"Trend","body":"Figure: Monthly cash in shows the trend. يوضح الشكل: الاتجاه الشهري.","panels":[0]}],"summary":"Chart: Branches lead."}
            """;
        var content = ReportComposer.Parse(json, 1, wantsEmail: false);
        Assert.Equal("Total cash in and Cash by branch summarise the year.", content.Introduction);
        Assert.Equal("Monthly cash in shows the trend. يوضح الاتجاه الشهري.", content.Sections[0].Body);
        Assert.Equal("Branches lead.", content.Summary);
        Assert.Equal("The figures in the table below", ReportComposer.WithoutCaptionLabels("The figures in the table below"));
    }

    // P13.17: chart labels are set by the document's text engine, so Arabic is shaped and laid out right to left.

    [Fact]
    public void Charts_KeepTheirLabelsApart_FromTheShapes_ForTheTextEngine()
    {
        string[] branches = ["الشارقة", "ديرة (فرع دبي الرئيسي)", "Al Ain"];
        var chart = SvgChartBuilder.Render("bar", [new ResultSeries("وصول", branches, [3, 2, 1]), new ResultSeries("مغادرة", branches, [2, 1, 1])], 480, 240);
        Assert.DoesNotContain("<text", chart.ShapesSvg, StringComparison.Ordinal);
        Assert.Contains("<rect", chart.ShapesSvg, StringComparison.Ordinal);
        Assert.Contains(chart.Texts, t => t.Text == "وصول" && t.Anchor == "start");
        Assert.Contains(chart.Texts, t => t.Text == "0" && t.Anchor == "end");
        Assert.All(branches, b => Assert.Contains(chart.Texts, t => t.Text.StartsWith(b.Split(' ')[0], StringComparison.Ordinal)));
        Assert.Equal(chart.Texts.Count, chart.Svg.Split("<text").Length - 1);

        var angled = SvgChartBuilder.Render("bar", [new ResultSeries("n", Enumerable.Range(1, 30).Select(i => $"موظف الصراف رقم {i}").ToList(), Enumerable.Range(1, 30).Select(i => (double)i).ToList())], 480, 240);
        Assert.Contains(angled.Texts, t => t.Rotation == -45 && t.Anchor == "end");

        Assert.True(ChartComposer.IsRightToLeft("ديرة (فرع دبي الرئيسي)"));
        Assert.True(ChartComposer.IsRightToLeft("2025 يناير"));
        Assert.False(ChartComposer.IsRightToLeft("Deira (Dubai Main)"));
        Assert.False(ChartComposer.IsRightToLeft("1.35B"));
    }

    [Fact]
    public void ChartImageRenderer_DrawsArabicLabels_WithTheTextEngine()
    {
        string[] months = ["2025-01-01", "2025-02-01", "2025-03-01"];
        var image = ChartImageRenderer.RenderImage("line", [new ResultSeries("الحركات", months, [1, 3, 2]), new ResultSeries("المغادرة", months, [1, 2, 2])], culture: CultureInfo.GetCultureInfo("ar-AE"));
        Assert.Equal(0x89, image.Png[0]);
        Assert.True(image.Png.Length > 10_000);
        var pie = ChartImageRenderer.RenderImage("pie", [new ResultSeries("x", ["في الموعد", "متأخر"], [90, 10])]);
        Assert.Equal(ChartImageRenderer.Height, pie.Height);
    }
}
