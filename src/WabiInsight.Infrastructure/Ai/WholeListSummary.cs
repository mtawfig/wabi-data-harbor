using Microsoft.Extensions.Logging;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Ai.Compilers;
using WabiInsight.Core.Organizations;

namespace WabiInsight.Infrastructure.Ai;

/// <summary>
/// For a list that stopped at the size limit (P13.50): asks the database how many rows the whole list has and what
/// its totals, averages and ranges are, so the answer states them for the whole list and not for the rows fetched.
/// One read-only query over the plan's own query, with the same row predicates. Nothing is asked for a list that is whole.
/// </summary>
public sealed partial class WholeListSummary
{
    private readonly IQueryExecutor _executor;
    private readonly ILogger<WholeListSummary> _logger;

    public WholeListSummary(IQueryExecutor executor, ILogger<WholeListSummary> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    /// <summary>The result with its whole-list count and totals, and the query that gave them (null when none was needed or it failed).</summary>
    public async Task<(AnalysisResult Result, CompiledQuery? Query)> CompleteAsync(Guid applicationId, ISqlDialectCompiler compiler, AuthorizedPlan authorized, AnalysisResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(authorized);
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Truncated)
        {
            return (result, null);
        }

        var numeric = result.Columns.Where(c => c.IsNumeric && authorized.Plan.Select.Any(s => string.Equals(s.Alias, c.Name, StringComparison.OrdinalIgnoreCase))).Select(c => c.Name).ToList();
        try
        {
            var compiled = compiler.CompileSummary(authorized, numeric);
            var summary = await _executor.ExecuteAsync(applicationId, compiled, null, cancellationToken);
            if (summary.Rows.Count == 0)
            {
                return (result, compiled);
            }

            var row = summary.Rows[0];
            double? Value(string name)
            {
                var index = summary.Columns.ToList().FindIndex(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                return index < 0 ? null : ResultCalculator.ToDouble(row[index]);
            }

            var kpis = new List<ResultKpi>();
            for (var i = 0; i < numeric.Count; i++)
            {
                var n = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (Value("s" + n) is not { } sum || Value("a" + n) is not { } average || Value("n" + n) is not { } lowest || Value("x" + n) is not { } highest)
                {
                    continue;
                }

                kpis.Add(new ResultKpi(numeric[i], "sum", Math.Round(sum, 4)));
                kpis.Add(new ResultKpi(numeric[i], "avg", Math.Round(average, 4)));
                kpis.Add(new ResultKpi(numeric[i], "min", lowest));
                kpis.Add(new ResultKpi(numeric[i], "max", highest));
            }

            var total = Value(SqlCompilerBase.SummaryRowsColumn) is { } rows ? (long)rows : (long?)null;
            return (result with { TotalRows = total, Kpis = kpis }, compiled);
        }
        catch (Exception ex) when (ex is AnalysisExecutionException or DomainException)
        {
            // The list itself was read; only its count and totals are missing. The answer stands without them.
            LogSummaryFailed(_logger, ex.Message);
            return (result, null);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The count and totals of a whole list could not be read ({Reason}); the answer is given without them.")]
    private static partial void LogSummaryFailed(ILogger logger, string reason);
}
