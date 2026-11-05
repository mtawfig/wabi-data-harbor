using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WabiInsight.Core.Entities.Insights;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Platform;

namespace WabiInsight.Core.Insights;

/// <summary>A fixed or ad-hoc filter on one approved column. Operators: eq, neq, in, gt, gte, lt, lte.</summary>
public sealed record MetricFilter(string Column, string Operator, IReadOnlyList<string> Values);

/// <summary>Resolved, approved shape of a view as the builder is allowed to see it.</summary>
public sealed record InsightsViewShape(string ViewName, IReadOnlyDictionary<string, string> ApprovedColumns);

public sealed record MetricDefinition(string Code, MetricAggregate Aggregate, string? Column, string? DateColumn, IReadOnlyList<MetricFilter> Filters);

public sealed record MetricQueryRequest(DateTimeOffset? From, DateTimeOffset? To, MetricBucket? Bucket, string? GroupBy, IReadOnlyList<MetricFilter>? Filters);

public sealed record BuiltQuery(string Sql, IReadOnlyDictionary<string, object> Parameters);

/// <summary>
/// Builds the only SQL ever sent to the insights views (specification §12.1). Identifiers come from the
/// approved catalog and are quoted; values are parameters; <c>[OrganizationId] = @tenant</c> is always the
/// first predicate and the organization column can neither be filtered nor grouped by. Pure and testable.
/// </summary>
public static partial class InsightsSqlBuilder
{
    public const string TenantParameter = "@tenant";
    public const string OrganizationColumn = "OrganizationId";
    private static readonly string[] Operators = ["eq", "neq", "in", "gt", "gte", "lt", "lte"];

    public static BuiltQuery Build(MetricDefinition metric, InsightsViewShape view, MetricQueryRequest request, Guid organizationId)
    {
        ArgumentNullException.ThrowIfNull(metric);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(request);
        if (!Identifier().IsMatch(view.ViewName))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "insights_view_invalid");
        }

        var parameters = new Dictionary<string, object>(StringComparer.Ordinal) { [TenantParameter] = organizationId };
        var select = new List<string>();
        var groupBy = new List<string>();
        var orderBy = new List<string>();

        if (request.Bucket is { } bucket)
        {
            var dateColumn = RequireDateColumn(metric, view, "insights_bucket_requires_date_column");
            var expression = BucketExpression(bucket, dateColumn);
            select.Add($"{expression} AS [Bucket]");
            groupBy.Add(expression);
            orderBy.Add("[Bucket]");
        }

        if (!string.IsNullOrWhiteSpace(request.GroupBy))
        {
            var column = Column(view, request.GroupBy, "insights_group_by_not_allowed");
            select.Add($"{column} AS [Group]");
            groupBy.Add(column);
            orderBy.Add("[Group]");
        }

        select.Add($"{AggregateExpression(metric, view)} AS [Value]");

        var where = new List<string> { $"[{OrganizationColumn}] = {TenantParameter}" };
        if (request.From is not null || request.To is not null)
        {
            var dateColumn = RequireDateColumn(metric, view, "insights_period_requires_date_column");
            if (request.From is { } from)
            {
                parameters["@from"] = from.UtcDateTime;
                where.Add($"{dateColumn} >= @from");
            }

            if (request.To is { } to)
            {
                parameters["@to"] = to.UtcDateTime;
                where.Add($"{dateColumn} < @to");
            }
        }

        var index = 0;
        foreach (var filter in metric.Filters.Concat(request.Filters ?? []))
        {
            where.Add(FilterPredicate(view, filter, parameters, ref index));
        }

        var sql = new StringBuilder();
        sql.Append("SELECT ").Append(string.Join(", ", select)).Append(" FROM [insights].[").Append(view.ViewName).Append(']');
        sql.Append(" WHERE ").Append(string.Join(" AND ", where));
        if (groupBy.Count > 0)
        {
            sql.Append(" GROUP BY ").Append(string.Join(", ", groupBy));
            sql.Append(" ORDER BY ").Append(string.Join(", ", orderBy));
        }

        return new BuiltQuery(sql.ToString(), parameters);
    }

    private static string AggregateExpression(MetricDefinition metric, InsightsViewShape view)
    {
        if (metric.Aggregate == MetricAggregate.Count)
        {
            return "COUNT_BIG(*)";
        }

        if (string.IsNullOrWhiteSpace(metric.Column))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "insights_metric_column_required");
        }

        var column = Column(view, metric.Column, "insights_column_not_allowed");
        return metric.Aggregate switch
        {
            MetricAggregate.CountDistinct => $"COUNT_BIG(DISTINCT {column})",
            MetricAggregate.Sum => $"SUM(CAST({column} AS bigint))",
            MetricAggregate.Average => $"AVG(CAST({column} AS float))",
            MetricAggregate.Min => $"MIN({column})",
            MetricAggregate.Max => $"MAX({column})",
            _ => throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "insights_aggregate_invalid"),
        };
    }

    private static string RequireDateColumn(MetricDefinition metric, InsightsViewShape view, string code)
        => string.IsNullOrWhiteSpace(metric.DateColumn) ? throw new DomainException(OrganizationOperationOutcome.ValidationFailed, code) : Column(view, metric.DateColumn, "insights_column_not_allowed");

    private static string BucketExpression(MetricBucket bucket, string column) => bucket switch
    {
        MetricBucket.Day => $"CAST({column} AS date)",
        MetricBucket.Week => $"DATEADD(week, DATEDIFF(week, 0, {column}), 0)",
        MetricBucket.Month => $"DATEFROMPARTS(YEAR({column}), MONTH({column}), 1)",
        _ => throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "insights_bucket_invalid"),
    };

    /// <summary>Resolves a column against the approved shape; the organization column is never addressable.</summary>
    private static string Column(InsightsViewShape view, string name, string code)
    {
        if (!Identifier().IsMatch(name) || string.Equals(name, OrganizationColumn, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, code);
        }

        var match = view.ApprovedColumns.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new DomainException(OrganizationOperationOutcome.ValidationFailed, code);
        return $"[{match}]";
    }

    private static string FilterPredicate(InsightsViewShape view, MetricFilter filter, Dictionary<string, object> parameters, ref int index)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var column = Column(view, filter.Column, "insights_column_not_allowed");
        var op = (filter.Operator ?? string.Empty).Trim().ToLowerInvariant();
        if (!Operators.Contains(op) || filter.Values is null || filter.Values.Count == 0 || (op != "in" && filter.Values.Count != 1))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "insights_filter_invalid");
        }

        var dataType = view.ApprovedColumns.First(k => string.Equals(k.Key, filter.Column, StringComparison.OrdinalIgnoreCase)).Value;
        if (op == "in")
        {
            var names = new List<string>();
            foreach (var value in filter.Values)
            {
                var name = $"@f{index++}";
                parameters[name] = Convert(value, dataType);
                names.Add(name);
            }

            return $"{column} IN ({string.Join(", ", names)})";
        }

        var parameter = $"@f{index++}";
        parameters[parameter] = Convert(filter.Values[0], dataType);
        var symbol = op switch { "eq" => "=", "neq" => "<>", "gt" => ">", "gte" => ">=", "lt" => "<", _ => "<=" };
        return $"{column} {symbol} {parameter}";
    }

    /// <summary>Typed parameter values so SQL Server never has to convert the column side of a predicate.</summary>
    private static object Convert(string value, string dataType)
    {
        try
        {
            return dataType.ToLowerInvariant() switch
            {
                "bit" => value is "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase),
                "int" => int.Parse(value, CultureInfo.InvariantCulture),
                "bigint" => long.Parse(value, CultureInfo.InvariantCulture),
                "uniqueidentifier" => Guid.Parse(value),
                "date" => DateOnly.Parse(value, CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue),
                "datetime2" or "datetimeoffset" => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture).UtcDateTime,
                "float" or "decimal" => double.Parse(value, CultureInfo.InvariantCulture),
                _ => value,
            };
        }
        catch (FormatException)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "insights_filter_invalid");
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    private static partial Regex Identifier();
}
