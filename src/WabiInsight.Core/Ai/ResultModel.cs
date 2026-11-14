using System.Globalization;

namespace WabiInsight.Core.Ai;

public sealed record ResultColumn(string Name, string DataType, bool IsNumeric, bool IsTemporal);

public sealed record ResultKpi(string Column, string Kind, double Value);

public sealed record ResultSeries(string Name, IReadOnlyList<string> Categories, IReadOnlyList<double> Values);

/// <summary>Structured result (specification §19 step 14): the table as returned, plus deterministic calculations made in code, never by the model.</summary>
public sealed record AnalysisResult(IReadOnlyList<ResultColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows, int RowCount, bool Truncated, IReadOnlyList<ResultKpi> Kpis, IReadOnlyList<ResultSeries> Series, double DurationMs, long? TotalRows = null)
{
    /// <summary>The most rows a table shows on the page and in a PDF or Word document; a longer list says so in one line and is whole in Excel (P13.50).</summary>
    public const int DisplayRows = 100;

    /// <summary>The most rows one Excel sheet holds besides its header row.</summary>
    public const long ExcelRows = 1_048_575;

    /// <summary>
    /// How many rows the whole list has: the rows fetched when the list is whole, the count the database gave for a
    /// list that stopped at the size limit (<see cref="TotalRows"/>), and null when that count could not be taken.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public long? WholeListRows => Truncated ? TotalRows : RowCount;

    /// <summary>True when the list is longer than the rows a table shows.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsLongList => Truncated || Rows.Count > DisplayRows;

    /// <summary>
    /// What a result means for the plan it answers (P13.49). A plan that asked for a number of rows ("top five") is
    /// complete with that number: it was not cut. A list with no limit of its own that stopped at the platform's
    /// size limit was cut: totals, averages and ranges of the rows fetched are not those of the whole list, so they
    /// are dropped here; the count and the totals of the whole list are then asked from the database (P13.50).
    /// </summary>
    public AnalysisResult For(QueryPlan plan, PlanLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!Truncated)
        {
            return this;
        }

        var askedFor = plan.Limit is { } limit && limit < (limits ?? new PlanLimits()).MaxRows;
        return askedFor ? this with { Truncated = false } : this with { Kpis = [] };
    }
}

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

    private static readonly string[] IsoDateTimes = ["yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF"];

    /// <summary>
    /// A cell as text. Dates read as dates: a stored answer carries them as ISO text ("2025-03-01T00:00:00"), which is
    /// written the way a date value is (2025-03-01, or 2025-03-01 06:15 when it has a time of day).
    /// </summary>
    public static string Format(object? value) => value switch
    {
        null => string.Empty,
        System.Text.Json.JsonElement element => element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => Format(element.GetString()),
            System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined => string.Empty,
            System.Text.Json.JsonValueKind.True => "True",
            System.Text.Json.JsonValueKind.False => "False",
            _ => element.GetRawText(),
        },
        string s when s.Length is >= 19 and <= 27 && s[10] == 'T' && DateTime.TryParseExact(s, IsoDateTimes, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => Format(parsed),
        DateTime dt => dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
