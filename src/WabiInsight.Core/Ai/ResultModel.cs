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
            var categories = rows.Select((r, index) => xIndex >= 0 ? Format(r[xIndex]) : (index + 1).ToString(CultureInfo.InvariantCulture)).ToList();
            foreach (var y in vis.Y)
            {
                var yIndex = columns.ToList().FindIndex(c => string.Equals(c.Name, y, StringComparison.OrdinalIgnoreCase));
                if (yIndex < 0 || !columns[yIndex].IsNumeric)
                {
                    continue;
                }

                series.Add(new ResultSeries(columns[yIndex].Name, categories, rows.Select(r => ToDouble(r[yIndex]) ?? 0d).ToList()));
            }
        }

        return (kpis, series);
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
