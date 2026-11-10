using WabiInsight.Core.Ai;
using WabiInsight.Core.Entities.Monitoring;
using WabiInsight.Core.Monitoring;
using WabiInsight.Infrastructure.Monitoring;

namespace WabiInsight.UnitTests.Monitoring;

/// <summary>P13.27: retries after a failed run, the digest's window, fixed dates in a scheduled analysis.</summary>
public sealed class SchedulerReliabilityTests
{
    private static readonly DateTimeOffset Finished = new(2026, 11, 10, 6, 0, 30, TimeSpan.Zero);

    private static ScheduledJob Daily() => new() { RecurrenceKind = JobRecurrenceKind.Daily, TimeOfDay = new TimeOnly(6, 0), TimeZone = "UTC" };

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 15)]
    [InlineData(3, 60)]
    public void AFailedDailyJob_IsRetriedAfterAShortGrowingDelay(int failures, int minutes)
    {
        Assert.Equal(Finished.AddMinutes(minutes), JobRecurrence.NextRunAfterFailure(Daily(), Finished, failures));
    }

    [Fact]
    public void AfterThreeRetries_TheJobWaitsForItsRegularSchedule()
    {
        var tomorrow = new DateTimeOffset(2026, 11, 11, 6, 0, 0, TimeSpan.Zero);
        Assert.Equal(tomorrow, JobRecurrence.NextRunAfterFailure(Daily(), Finished, 4));
        Assert.Equal(tomorrow, JobRecurrence.NextRunAfterFailure(Daily(), Finished, 25));
        Assert.Equal(tomorrow, JobRecurrence.NextRunAfterFailure(Daily(), Finished, 0)); // not a failure: the regular run
    }

    [Fact]
    public void ARetry_IsNeverLaterThanTheRegularRun()
    {
        // Every two minutes: the regular run comes before the five-minute retry.
        var frequent = new ScheduledJob { RecurrenceKind = JobRecurrenceKind.Interval, IntervalMinutes = 2, TimeZone = "UTC" };
        Assert.Equal(Finished.AddMinutes(2), JobRecurrence.NextRunAfterFailure(frequent, Finished, 1));

        // A daily job that failed 20 minutes before its next regular run: the hour-long third retry gives way to it.
        var lateFailure = new DateTimeOffset(2026, 11, 11, 5, 40, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 11, 11, 6, 0, 0, TimeSpan.Zero), JobRecurrence.NextRunAfterFailure(Daily(), lateFailure, 3));
    }

    [Fact]
    public void TheDigest_CountsFromTheLastOneThatWentOut()
    {
        var now = new DateTimeOffset(2026, 11, 10, 7, 0, 0, TimeSpan.Zero);
        // Yesterday's digest went out: everything since then.
        Assert.Equal(now.AddDays(-1), AlertDigestJob.DigestWindowStart(now.AddDays(-1), now));
        // The server was down for three days (or three digests failed): nothing in between is dropped.
        Assert.Equal(now.AddDays(-3), AlertDigestJob.DigestWindowStart(now.AddDays(-3), now));
        // Never more than a week back.
        Assert.Equal(now.AddDays(-7), AlertDigestJob.DigestWindowStart(now.AddDays(-30), now));
        // The first digest of an organization: the last 24 hours.
        Assert.Equal(now.AddHours(-24), AlertDigestJob.DigestWindowStart(null, now));
    }

    [Fact]
    public void FixedDates_AreFoundInConditionValues_AndRelativePeriodsAreNot()
    {
        const string Plan = """
            {"objective":"Sales on 2026-02-14","queries":[{"title":"From 2025-12-31","plan":{
              "filters":{"logic":"and","conditions":[
                {"column":"s.SoldAt","operator":"between","values":["2026-03-31","2026-01-01T00:00:00"]},
                {"column":"s.Region","operator":"eq","values":["2026"]},
                {"column":"s.SoldAt","operator":"gte","values":["$month_start-1m"]}],
               "groups":[{"logic":"or","conditions":[{"column":"s.ClosedAt","operator":"lt","values":["2026-01-01"]}],"groups":[]}]},
              "having":[{"column":"total","operator":"gt","values":["100"]}]}}]}
            """;
        // Earliest first, each date once; the dates in the objective and the title, the year and the relative period are not conditions on a date.
        Assert.Equal(["2026-01-01", "2026-03-31"], PlanDates.FixedDates(Plan));

        Assert.Empty(PlanDates.FixedDates("""{"filters":{"conditions":[{"column":"s.SoldAt","operator":"gte","values":["$year_start"]},{"column":"s.SoldAt","operator":"lt","values":["$today"]}]}}"""));
        Assert.Empty(PlanDates.FixedDates("""{"filters":{"conditions":[{"column":"s.Code","operator":"eq","values":["2026-13-45","20260101","2026-1-1"]}]}}"""));
        Assert.Empty(PlanDates.FixedDates(null));
        Assert.Empty(PlanDates.FixedDates("not json"));
        Assert.Equal(PlanDates.MaxReported, PlanDates.FixedDates("{\"values\":[" + string.Join(',', Enumerable.Range(1, 20).Select(d => $"\"2026-01-{d:00}\"")) + "]}").Count);
    }

    [Fact]
    public void AReportRunIsRetried_OnlyWhenTryingAgainCanHelp()
    {
        static ReportDelivery Delivery(ReportDeliveryStatus status) => new() { Status = status };
        var run = Guid.NewGuid();
        // The source did not answer for anyone: worth another try.
        Assert.True(PeriodicReportRunner.IsWorthRetrying(new SubscriptionRunResult(run, [Delivery(ReportDeliveryStatus.Failed), Delivery(ReportDeliveryStatus.Failed)], null)));
        // Someone already received theirs: a retry would send it twice.
        Assert.False(PeriodicReportRunner.IsWorthRetrying(new SubscriptionRunResult(run, [Delivery(ReportDeliveryStatus.Delivered), Delivery(ReportDeliveryStatus.Failed)], null)));
        Assert.False(PeriodicReportRunner.IsWorthRetrying(new SubscriptionRunResult(run, [Delivery(ReportDeliveryStatus.NoData)], null)));
        // Nothing that an hour will change.
        foreach (var permanent in new[] { PeriodicReportRunner.ShareEnded, "subscription_analysis_missing", "subscription_analysis_has_no_plan", "subscription_no_active_recipients", "organization_inactive" })
        {
            Assert.False(PeriodicReportRunner.IsWorthRetrying(new SubscriptionRunResult(run, [], permanent)), permanent);
        }
    }
}
