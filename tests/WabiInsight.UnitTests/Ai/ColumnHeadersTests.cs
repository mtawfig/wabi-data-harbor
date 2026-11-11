using WabiInsight.Core.Ai;
using WabiInsight.Infrastructure.Reports;

namespace WabiInsight.UnitTests.Ai;

public sealed class ColumnHeadersTests
{
    private static AnalysisPanel Panel()
    {
        var plan = new QueryPlan
        {
            Title = "Deposits",
            Objective = "Deposits by branch and direction",
            Select =
            [
                new PlanSelectItem { Alias = "branch_name", Column = "b.BranchName" },
                new PlanSelectItem { Alias = "direction", Column = "t.Direction" },
                new PlanSelectItem { Alias = "total_amount", Column = "t.Amount", Aggregate = "sum" },
                new PlanSelectItem { Alias = "transactions", Aggregate = "count" },
                new PlanSelectItem { Alias = "month", Column = "t.PostedAt", DateTrunc = "month" },
            ],
            Visualization = new PlanVisualization { Type = "bar", X = "branch_name", Y = ["total_amount", "transactions"] },
        };
        var columns = new[]
        {
            new ResultColumn("branch_name", "nvarchar", false, false), new ResultColumn("direction", "nvarchar", false, false),
            new ResultColumn("total_amount", "decimal", true, false), new ResultColumn("transactions", "int", true, false), new ResultColumn("month", "date", false, true),
        };
        var rows = new List<IReadOnlyList<object?>> { new object?[] { "Dubai", "In", 10m, 1, "2026-01-01" }, new object?[] { "Dubai", "Out", 4m, 2, "2026-01-01" }, new object?[] { "Sharjah", "In", 7m, 1, "2026-01-01" } };
        var (kpis, series) = ResultCalculator.Calculate(columns, rows, plan.Visualization);
        var labels = new Dictionary<string, DisplayLabel>(StringComparer.OrdinalIgnoreCase)
        {
            ["b.BranchName"] = new("Branch", "الفرع"),
            ["t.Amount"] = new("Amount", "المبلغ"),
            ["t.PostedAt"] = new("Posting date", null),
        };
        return new AnalysisPanel("Deposits", plan, new AnalysisResult(columns, rows, rows.Count, false, kpis, series, 1), Labels: labels);
    }

    [Fact]
    public void Arabic_NamesEveryColumnFromThePlanAndTheCatalogue()
    {
        var headers = ColumnHeaders.For(Panel(), arabic: true);
        Assert.Equal("الفرع", headers["branch_name"]);
        Assert.Equal("إجمالي المبلغ", headers["TOTAL_AMOUNT"]);
        Assert.Equal("عدد الصفوف", headers["transactions"]);
        Assert.Equal("Posting date حسب الشهر", headers["month"]);
        Assert.Equal("Direction", headers["direction"]);
        Assert.Equal("Unknown column", ColumnHeaders.Header(headers, "Unknown_column"));
    }

    [Fact]
    public void English_KeepsThePlannersAliasUnlessAPlainColumnHasACatalogueName()
    {
        var headers = ColumnHeaders.For(Panel(), arabic: false);
        Assert.Equal("Branch", headers["branch_name"]);
        Assert.Equal("Total amount", headers["total_amount"]);
        Assert.Equal("Month", headers["month"]);
    }

    [Fact]
    public void Series_AreNamedAfterTheirMeasureInTheDocumentsLanguage()
    {
        var series = ColumnHeaders.Series(Panel(), arabic: true);
        Assert.Equal(["إجمالي المبلغ · In", "إجمالي المبلغ · Out", "عدد الصفوف · In", "عدد الصفوف · Out"], series.Select(s => s.Name));
        Assert.Equal("Sales", ColumnHeaders.SeriesName(new Dictionary<string, string>(), "Sales"));
    }
}
