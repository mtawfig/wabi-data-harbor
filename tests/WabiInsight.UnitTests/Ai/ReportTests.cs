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
        Assert.Equal("report-20261106.pdf", Infrastructure.Ai.AnalysisService.FileName("دراسة", new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal("Monthly Traffic", QuestPdfReportRenderer.StripNumbering("2. 2. Monthly Traffic"));
        Assert.Equal("Overview", QuestPdfReportRenderer.StripNumbering("Overview"));
    }
}
