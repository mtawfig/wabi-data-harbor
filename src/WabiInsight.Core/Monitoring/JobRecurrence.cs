using WabiInsight.Core.Entities.Monitoring;

namespace WabiInsight.Core.Monitoring;

/// <summary>
/// Computes the next run of a scheduled job strictly after a point in time, in the job's time zone. Daily,
/// weekly and monthly schedules follow the organization's local clock (so a 02:15 job stays at 02:15 across
/// daylight-saving changes); interval schedules are plain UTC arithmetic. An unknown time zone falls back to UTC.
/// </summary>
public static class JobRecurrence
{
    public static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    public static DateTimeOffset NextRun(ScheduledJob job, DateTimeOffset after)
    {
        ArgumentNullException.ThrowIfNull(job);
        return NextRun(job.RecurrenceKind, job.IntervalMinutes, job.TimeOfDay, job.DayOfWeek, job.DayOfMonth, ResolveTimeZone(job.TimeZone), after);
    }

    public static DateTimeOffset NextRun(JobRecurrenceKind kind, int intervalMinutes, TimeOnly timeOfDay, int? dayOfWeek, int? dayOfMonth, TimeZoneInfo timeZone, DateTimeOffset after)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        if (kind == JobRecurrenceKind.Interval)
        {
            return after.AddMinutes(Math.Clamp(intervalMinutes, 1, 60 * 24 * 31));
        }

        var local = TimeZoneInfo.ConvertTime(after, timeZone).DateTime;
        var date = DateOnly.FromDateTime(local);
        for (var i = 0; i < 400; i++)
        {
            var candidateDate = kind switch
            {
                JobRecurrenceKind.Weekly => date,
                JobRecurrenceKind.Monthly => date,
                _ => date,
            };

            var matches = kind switch
            {
                JobRecurrenceKind.Weekly => IsoDayOfWeek(candidateDate) == Math.Clamp(dayOfWeek ?? 1, 1, 7),
                JobRecurrenceKind.Monthly => candidateDate.Day == Math.Min(Math.Clamp(dayOfMonth ?? 1, 1, 31), DateTime.DaysInMonth(candidateDate.Year, candidateDate.Month)),
                _ => true,
            };

            if (matches)
            {
                var candidate = ToUtc(candidateDate.ToDateTime(timeOfDay), timeZone);
                if (candidate > after)
                {
                    return candidate;
                }
            }

            date = date.AddDays(1);
        }

        return after.AddDays(1);
    }

    /// <summary>Minutes to wait before the first, second and third retry of a failed run.</summary>
    public static readonly IReadOnlyList<int> RetryDelaysMinutes = [5, 15, 60];

    /// <summary>
    /// When a job that just failed runs again (P13.27). The first failures in a row are retried after a short, growing
    /// delay, never later than the regular next run; after that the job waits for its regular schedule, so a source
    /// that stays down is not hammered. <paramref name="consecutiveFailures"/> includes the failure that just happened.
    /// </summary>
    public static DateTimeOffset NextRunAfterFailure(ScheduledJob job, DateTimeOffset finished, int consecutiveFailures)
    {
        ArgumentNullException.ThrowIfNull(job);
        var regular = NextRun(job, finished);
        if (consecutiveFailures < 1 || consecutiveFailures > RetryDelaysMinutes.Count)
        {
            return regular;
        }

        var retry = finished.AddMinutes(RetryDelaysMinutes[consecutiveFailures - 1]);
        return retry < regular ? retry : regular;
    }

    /// <summary>1 = Monday … 7 = Sunday, matching the platform's calendar convention.</summary>
    public static int IsoDayOfWeek(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;

    private static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo timeZone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(unspecified))
        {
            // The clock skipped this time (spring forward): run an hour later.
            unspecified = unspecified.AddHours(1);
        }

        if (timeZone.IsAmbiguousTime(unspecified))
        {
            // Fall back: take the first occurrence (the larger offset).
            var offset = timeZone.GetAmbiguousTimeOffsets(unspecified).Max();
            return new DateTimeOffset(unspecified, offset).ToUniversalTime();
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, timeZone), TimeSpan.Zero);
    }
}
