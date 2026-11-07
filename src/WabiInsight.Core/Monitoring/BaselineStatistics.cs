namespace WabiInsight.Core.Monitoring;

/// <summary>Daily activity of one subject on one day; the unit baselines are computed from.</summary>
public sealed record DailyActivity(int Queries, long Rows, int RestrictedTouches, int Exports, int Unauthorized);

/// <summary>Median and 95th percentile of each daily measure over the subject's active days.</summary>
public sealed record BaselineSummary(
    int ActiveDays,
    double QueriesMedian,
    double QueriesP95,
    double RowsMedian,
    double RowsP95,
    double RestrictedMedian,
    double RestrictedP95,
    double ExportsMedian,
    double ExportsP95,
    double UnauthorizedMedian,
    double UnauthorizedP95);

/// <summary>Pure percentile arithmetic for baselines (linear interpolation between ranks).</summary>
public static class BaselineStatistics
{
    public static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return 0;
        }

        if (percentile is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile), "Percentile must be between 0 and 1.");
        }

        var sorted = values.Order().ToArray();
        var rank = percentile * (sorted.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper)
        {
            return sorted[lower];
        }

        var weight = rank - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * weight;
    }

    public static BaselineSummary Summarize(IReadOnlyList<DailyActivity> days)
    {
        ArgumentNullException.ThrowIfNull(days);
        double[] Pick(Func<DailyActivity, double> selector) => days.Select(selector).ToArray();
        var queries = Pick(d => d.Queries);
        var rows = Pick(d => d.Rows);
        var restricted = Pick(d => d.RestrictedTouches);
        var exports = Pick(d => d.Exports);
        var unauthorized = Pick(d => d.Unauthorized);
        return new BaselineSummary(
            days.Count,
            Percentile(queries, 0.5), Percentile(queries, 0.95),
            Percentile(rows, 0.5), Percentile(rows, 0.95),
            Percentile(restricted, 0.5), Percentile(restricted, 0.95),
            Percentile(exports, 0.5), Percentile(exports, 0.95),
            Percentile(unauthorized, 0.5), Percentile(unauthorized, 0.95));
    }
}
