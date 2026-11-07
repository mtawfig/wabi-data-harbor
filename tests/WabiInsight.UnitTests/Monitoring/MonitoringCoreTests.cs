using WabiInsight.Core.Ai;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Monitoring;
using WabiInsight.Core.Monitoring;

namespace WabiInsight.UnitTests.Monitoring;

public sealed class AccessFactExtractorTests
{
    private static QueryPlan PayslipsJoinedToEmployees() => new()
    {
        Source = new PlanObjectRef { Schema = "dbo", ObjectName = "Payslips", Alias = "p" },
        Joins = [new PlanJoin { Schema = "dbo", ObjectName = "Employees", Alias = "e", On = [new PlanJoinCondition { Left = "p.EmployeeNumber", Right = "e.EmployeeNumber" }] }],
        Select = [new PlanSelectItem { Alias = "name", Column = "e.FullName" }, new PlanSelectItem { Alias = "net", Column = "p.NetPay", Aggregate = "sum" }],
        Filters = new PlanFilterGroup
        {
            Conditions = [new PlanCondition { Column = "p.EmployeeNumber", Operator = "in", Values = ["E1001", "E1002", "E1003"] }, new PlanCondition { Column = "PeriodCode", Operator = "eq", Values = ["2025-01"] }],
            Groups = [new PlanFilterGroup { Logic = "or", Conditions = [new PlanCondition { Column = "e.DepartmentCode", Operator = "eq", Values = ["HR"] }] }],
        },
        GroupBy = ["name"],
    };

    [Fact]
    public void Summarize_AttributesColumnsAndFilterValues_ToTheRightObjects()
    {
        var summaries = AccessFactExtractor.Summarize([new PlanExecution(PayslipsJoinedToEmployees(), 42)]);

        var payslips = Assert.Single(summaries, s => s.ObjectName == "Payslips");
        var employees = Assert.Single(summaries, s => s.ObjectName == "Employees");
        Assert.True(payslips.IsPrimary);
        Assert.Equal(42, payslips.RowCount);
        Assert.False(employees.IsPrimary);
        Assert.Equal(0, employees.RowCount);
        // Bare column names belong to the source; join columns count on both sides.
        Assert.Equal(["EmployeeNumber", "NetPay", "PeriodCode"], payslips.Columns.Order().ToArray());
        Assert.Equal(["DepartmentCode", "EmployeeNumber", "FullName"], employees.Columns.Order().ToArray());
        Assert.Equal(3, payslips.FilterValueCounts["EmployeeNumber"]);
        Assert.Equal(1, payslips.FilterValueCounts["PeriodCode"]);
        Assert.Equal(1, employees.FilterValueCounts["DepartmentCode"]);
        Assert.Equal(["EmployeeNumber", "PeriodCode"], payslips.FilterColumns.Order().ToArray());
    }

    [Fact]
    public void Summarize_MergesSeveralQueriesOfOneAnalysis()
    {
        var second = new QueryPlan { Source = new PlanObjectRef { Schema = "dbo", ObjectName = "Employees", Alias = "x" }, Select = [new PlanSelectItem { Alias = "c", Aggregate = "count" }] };
        var summaries = AccessFactExtractor.Summarize([new PlanExecution(PayslipsJoinedToEmployees(), 10), new PlanExecution(second, 599)]);

        var employees = Assert.Single(summaries, s => s.ObjectName == "Employees");
        Assert.True(employees.IsPrimary);
        Assert.Equal(599, employees.RowCount);
        Assert.Equal(2, summaries.Count);
    }
}

public sealed class BaselineStatisticsTests
{
    [Theory]
    [InlineData(0.5, 3)]
    [InlineData(0.95, 4.8)]
    [InlineData(0, 1)]
    [InlineData(1, 5)]
    public void Percentile_InterpolatesBetweenRanks(double percentile, double expected)
    {
        Assert.Equal(expected, BaselineStatistics.Percentile([5, 1, 4, 2, 3], percentile), 6);
    }

    [Fact]
    public void Percentile_OfNothing_IsZero_AndSummaryCountsActiveDays()
    {
        Assert.Equal(0, BaselineStatistics.Percentile([], 0.5));
        var summary = BaselineStatistics.Summarize([new DailyActivity(2, 100, 0, 0, 0), new DailyActivity(4, 300, 1, 1, 2), new DailyActivity(6, 500, 0, 0, 0)]);
        Assert.Equal(3, summary.ActiveDays);
        Assert.Equal(4, summary.QueriesMedian);
        Assert.Equal(300, summary.RowsMedian);
        Assert.Equal(1.8, summary.UnauthorizedP95, 6); // [0, 0, 2]: rank 1.9 → 0 + 0.9 × 2
    }
}

public sealed class JobRecurrenceTests
{
    private static readonly TimeZoneInfo Dubai = JobRecurrence.ResolveTimeZone("Asia/Dubai");

    [Fact]
    public void Daily_RunsAtTheLocalTime_StrictlyAfterNow()
    {
        // 2026-03-10 01:00 Dubai (= 2026-03-09 21:00 UTC): today's 02:15 is still ahead.
        var after = new DateTimeOffset(2026, 3, 9, 21, 0, 0, TimeSpan.Zero);
        var next = JobRecurrence.NextRun(JobRecurrenceKind.Daily, 0, new TimeOnly(2, 15), null, null, Dubai, after);
        Assert.Equal(new DateTimeOffset(2026, 3, 9, 22, 15, 0, TimeSpan.Zero), next);

        // Exactly at 02:15 Dubai: the next run is tomorrow, so a run never repeats within the same slot.
        var atSlot = next;
        Assert.Equal(next.AddDays(1), JobRecurrence.NextRun(JobRecurrenceKind.Daily, 0, new TimeOnly(2, 15), null, null, Dubai, atSlot));
    }

    [Fact]
    public void MissedRuns_CollapseToOne()
    {
        // The API was down for three days; the next run is the next slot after "now", not three catch-up runs.
        var after = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);
        var next = JobRecurrence.NextRun(JobRecurrenceKind.Daily, 0, new TimeOnly(3, 30), null, null, TimeZoneInfo.Utc, after);
        Assert.Equal(new DateTimeOffset(2026, 5, 21, 3, 30, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Weekly_AndMonthly_PickTheRightDay()
    {
        var after = new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero); // a Saturday
        var monday = JobRecurrence.NextRun(JobRecurrenceKind.Weekly, 0, new TimeOnly(7, 0), 1, null, TimeZoneInfo.Utc, after);
        Assert.Equal(DayOfWeek.Monday, monday.DayOfWeek);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 7, 0, 0, TimeSpan.Zero), monday);

        // Day 31 in a 30-day month runs on the 30th.
        var monthly = JobRecurrence.NextRun(JobRecurrenceKind.Monthly, 0, new TimeOnly(1, 0), null, 31, TimeZoneInfo.Utc, after);
        Assert.Equal(new DateTimeOffset(2026, 11, 30, 1, 0, 0, TimeSpan.Zero), monthly);
    }

    [Fact]
    public void Interval_AndUnknownTimeZone()
    {
        var after = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(after.AddMinutes(15), JobRecurrence.NextRun(JobRecurrenceKind.Interval, 15, default, null, null, TimeZoneInfo.Utc, after));
        Assert.Equal(TimeZoneInfo.Utc, JobRecurrence.ResolveTimeZone("Mars/Olympus"));
        Assert.Equal(7, JobRecurrence.IsoDayOfWeek(new DateOnly(2026, 11, 8)));
    }
}

public sealed class ColumnRoleSuggesterTests
{
    [Theory]
    [InlineData("AccountId", "int", false, ColumnSemanticRole.AccountIdentifier)]
    [InlineData("IBAN", "nvarchar", false, ColumnSemanticRole.AccountIdentifier)]
    [InlineData("DeviceId", "nvarchar", false, ColumnSemanticRole.DeviceIdentifier)]
    [InlineData("ip_address", "varchar", false, ColumnSemanticRole.DeviceIdentifier)]
    [InlineData("EmployeeNumber", "nvarchar", false, ColumnSemanticRole.PersonIdentifier)]
    [InlineData("CustomerID", "int", true, ColumnSemanticRole.PersonIdentifier)]
    [InlineData("PassportNumber", "nvarchar", false, ColumnSemanticRole.PersonIdentifier)]
    [InlineData("LoginAt", "datetime2", false, ColumnSemanticRole.Timestamp)]
    [InlineData("FlightDate", "date", false, ColumnSemanticRole.Timestamp)]
    [InlineData("NetPay", "decimal", false, ColumnSemanticRole.Amount)]
    [InlineData("Passengers", "int", false, ColumnSemanticRole.Quantity)]
    [InlineData("Status", "nvarchar", false, ColumnSemanticRole.Status)]
    [InlineData("AirportCode", "char", false, ColumnSemanticRole.Location)]
    [InlineData("WorkEmail", "nvarchar", false, ColumnSemanticRole.Contact)]
    [InlineData("FlightId", "int", true, ColumnSemanticRole.Identifier)]
    [InlineData("Remarks", "nvarchar", false, ColumnSemanticRole.Other)]
    public void Suggest_UsesNameTypeAndKey(string name, string type, bool primaryKey, ColumnSemanticRole expected)
    {
        Assert.Equal(expected, ColumnRoleSuggester.Suggest(name, type, primaryKey));
    }

    [Fact]
    public void Normalize_SplitsCamelCaseAndSeparators()
    {
        Assert.Equal("account no", ColumnRoleSuggester.Normalize("AccountNo"));
        Assert.Equal("account no", ColumnRoleSuggester.Normalize("account_no"));
        Assert.Equal("login at", ColumnRoleSuggester.Normalize("Login_At2"));
    }

    [Fact]
    public void IdentifierLike_CoversKeysAndIdentifierRoles()
    {
        Assert.True(new DatabaseColumn { IsPrimaryKey = true }.IsIdentifierLike);
        Assert.True(new DatabaseColumn { SemanticRole = ColumnSemanticRole.AccountIdentifier }.IsIdentifierLike);
        Assert.False(new DatabaseColumn { SemanticRole = ColumnSemanticRole.Amount }.IsIdentifierLike);
        Assert.False(new DatabaseColumn().IsIdentifierLike);
    }
}
