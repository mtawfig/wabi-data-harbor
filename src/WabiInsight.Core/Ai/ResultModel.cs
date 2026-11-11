using System.Globalization;

namespace WabiInsight.Core.Ai;

public sealed record ResultColumn(string Name, string DataType, bool IsNumeric, bool IsTemporal);

public sealed record ResultKpi(string Column, string Kind, double Value);

public sealed record ResultSeries(string Name, IReadOnlyList<string> Categories, IReadOnlyList<double> Values);

/// <summary>Structured result (specification §19 step 14): the table as returned, plus deterministic calculations made in code, never by the model.</summary>
public sealed record AnalysisResult(IReadOnlyList<ResultColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows, int RowCount, bool Truncated, IReadOnlyList<ResultKpi> Kpis, IReadOnlyList<ResultSeries> Series, double DurationMs);

/// <summary>Deterministic calculations over the executed rows: totals, averages, ranges and chart series from the plan's visualization hint.</summary>
public static class ResultCalculator
{
    public static (IReadOnlyList<ResultKpi> Kpis, IReadOnlyList<ResultSeries> Series) Calculate(IReadOnlyList<ResultColumn> columns, IReadOnlyList<IReadOnlyList<object?>> rows, PlanVisualization? visualization)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        var kpis = new List<ResultKpi>();
        for (var i = 0; i < columns.Count; i++)
        {
            if (!columns[i].IsNumeric)
            {
                continue;
            }

            var values = rows.Select(r => ToDouble(r[i])).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            if (values.Count == 0)
            {
                continue;
            }

            kpis.Add(new ResultKpi(columns[i].Name, "sum", Math.Round(values.Sum(), 4)));
            kpis.Add(new ResultKpi(columns[i].Name, "avg", Math.Round(values.Average(), 4)));
            kpis.Add(new ResultKpi(columns[i].Name, "min", values.Min()));
            kpis.Add(new ResultKpi(columns[i].Name, "max", values.Max()));
        }

        var series = new List<ResultSeries>();
        if (visualization is { } vis && vis.Type is "bar" or "line" or "pie" && rows.Count > 0)
        {
            var xIndex = vis.X is null ? -1 : columns.ToList().FindIndex(c => string.Equals(c.Name, vis.X, StringComparison.OrdinalIgnoreCase));
            var yIndexes = vis.Y.Select(y => columns.ToList().FindIndex(c => string.Equals(c.Name, y, StringComparison.OrdinalIgnoreCase))).Where(i => i >= 0 && columns[i].IsNumeric).Distinct().ToList();
            var keyIndex = vis.Type == "pie" ? -1 : SeriesKeyIndex(columns, rows, xIndex, yIndexes);
            if (keyIndex >= 0)
            {
                // A second category column (direction, status, ...) becomes one series per value over the distinct x values.
                var categories = rows.Select(r => xIndex >= 0 ? Format(r[xIndex]) : string.Empty).Distinct().ToList();
                var keys = rows.Select(r => Format(r[keyIndex])).Distinct().ToList();
                foreach (var yIndex in yIndexes)
                {
                    // Rows that share a series value and a category (a third column the chart does not show) add up.
                    var totals = new Dictionary<(string Key, string Category), double>();
                    foreach (var row in rows)
                    {
                        var cell = (Format(row[keyIndex]), xIndex >= 0 ? Format(row[xIndex]) : string.Empty);
                        totals[cell] = totals.GetValueOrDefault(cell) + (ToDouble(row[yIndex]) ?? 0d);
                    }

                    foreach (var key in keys)
                    {
                        var name = yIndexes.Count == 1 ? key : $"{columns[yIndex].Name} · {key}";
                        series.Add(new ResultSeries(name, categories, categories.Select(c => Math.Round(totals.GetValueOrDefault((key, c)), 4)).ToList()));
                    }
                }
            }
            else
            {
                if (xIndex < 0)
                {
                    var positions = rows.Select((_, index) => (index + 1).ToString(CultureInfo.InvariantCulture)).ToList();
                    foreach (var yIndex in yIndexes)
                    {
                        series.Add(new ResultSeries(columns[yIndex].Name, positions, rows.Select(r => ToDouble(r[yIndex]) ?? 0d).ToList()));
                    }
                }
                else
                {
                    // One bar, point or slice per category, in first-seen order; rows that repeat a category add up instead of drawing it twice.
                    var categories = rows.Select(r => Format(r[xIndex])).Distinct().ToList();
                    foreach (var yIndex in yIndexes)
                    {
                        var totals = new Dictionary<string, double>();
                        foreach (var row in rows)
                        {
                            var category = Format(row[xIndex]);
                            totals[category] = totals.GetValueOrDefault(category) + (ToDouble(row[yIndex]) ?? 0d);
                        }

                        series.Add(new ResultSeries(columns[yIndex].Name, categories, categories.Select(c => Math.Round(totals[c], 4)).ToList()));
                    }
                }
            }
        }

        return (kpis, series);
    }

    /// <summary>The first non-numeric column that is neither x nor a measure and has few distinct values; -1 when the rows are a plain one-series shape.</summary>
    private static int SeriesKeyIndex(IReadOnlyList<ResultColumn> columns, IReadOnlyList<IReadOnlyList<object?>> rows, int xIndex, List<int> yIndexes)
    {
        const int maxKeys = 12;
        for (var i = 0; i < columns.Count; i++)
        {
            if (i == xIndex || yIndexes.Contains(i) || columns[i].IsNumeric)
            {
                continue;
            }

            var distinct = rows.Select(r => Format(r[i])).Distinct().Count();
            var xDistinct = xIndex >= 0 ? rows.Select(r => Format(r[xIndex])).Distinct().Count() : rows.Count;
            if (distinct is > 1 and <= maxKeys && xDistinct < rows.Count)
            {
                return i;
            }
        }

        return -1;
    }

    public static double? ToDouble(object? value) => value switch
    {
        null => null,
        double d => d,
        float f => f,
        decimal m => (double)m,
        long l => l,
        int i => i,
        short s => s,
        byte b => b,
        bool bo => bo ? 1 : 0,
        _ => double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
    };

    public static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTime dt => dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
