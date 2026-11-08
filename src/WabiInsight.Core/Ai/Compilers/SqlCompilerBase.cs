using System.Globalization;
using System.Text;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Organizations;

namespace WabiInsight.Core.Ai.Compilers;

/// <summary>Shared compilation logic; dialects override quoting, parameter prefix, date truncation and limits.</summary>
public abstract class SqlCompilerBase : ISqlDialectCompiler
{
    private static readonly string[] DateTypes = ["date", "datetime", "datetime2", "datetimeoffset", "smalldatetime", "timestamp", "timestamp(6)", "timestamp with time zone", "timestamp(6) with time zone"];
    private static readonly string[] IntegerTypes = ["int", "bigint", "smallint", "tinyint", "integer", "number"];
    private static readonly string[] DecimalTypes = ["decimal", "numeric", "money", "smallmoney", "float", "real", "number", "binary_double", "binary_float"];
    private static readonly string[] BooleanTypes = ["bit"];

    public abstract DatabaseType DatabaseType { get; }

    protected abstract string Quote(string identifier);

    protected abstract string ParameterPrefix { get; }

    protected abstract string DateTrunc(string unit, string column);

    protected abstract string ApplyLimit(string selectWithoutLimit, int limit, bool hasOrderBy);

    public CompiledQuery Compile(AuthorizedPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var context = new CompileContext(ParameterPrefix);
        var objects = plan.Objects.ToDictionary(o => o.Alias, StringComparer.OrdinalIgnoreCase);
        var query = plan.Plan;

        string Column(string reference)
        {
            var split = QueryPlanValidator.Split(reference);
            if (!objects.TryGetValue(split.Alias, out var o) || !o.ColumnTypes.ContainsKey(split.Column))
            {
                throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "column_not_permitted");
            }

            var name = o.ColumnTypes.Keys.First(k => string.Equals(k, split.Column, StringComparison.OrdinalIgnoreCase));
            return $"{Quote(o.Alias)}.{Quote(name)}";
        }

        string ColumnType(string reference)
        {
            var split = QueryPlanValidator.Split(reference);
            return objects[split.Alias].ColumnTypes.First(k => string.Equals(k.Key, split.Column, StringComparison.OrdinalIgnoreCase)).Value;
        }

        // SUM, AVG, MIN and MAX reject bit columns in SQL Server: count the true values (SUM), give their share (AVG, 0 to 1).
        string Numeric(string reference, string target)
            => BooleanTypes.Contains(ColumnType(reference).ToLowerInvariant()) ? $"CAST({Column(reference)} AS {target})" : Column(reference);

        // SELECT
        var select = new List<string>();
        var aliasExpressions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in query.Select)
        {
            string expression;
            if (item.Aggregate is not null)
            {
                var aggregate = item.Aggregate.ToLowerInvariant();
                expression = aggregate switch
                {
                    "count" => item.Column is null ? "COUNT(*)" : $"COUNT({Column(item.Column)})",
                    "count_distinct" => $"COUNT(DISTINCT {Column(item.Column!)})",
                    "sum" => $"SUM({Numeric(item.Column!, "INT")})",
                    "avg" => $"AVG({Numeric(item.Column!, "FLOAT")})",
                    "min" => $"MIN({Numeric(item.Column!, "INT")})",
                    "max" => $"MAX({Numeric(item.Column!, "INT")})",
                    _ => throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "plan_aggregate_invalid"),
                };
            }
            else if (item.DateTrunc is not null)
            {
                expression = DateTrunc(item.DateTrunc.ToLowerInvariant(), Column(item.Column!));
            }
            else
            {
                expression = Column(item.Column!);
            }

            aliasExpressions[item.Alias] = expression;
            select.Add($"{expression} AS {Quote(item.Alias)}");
        }

        // FROM: each object is a derived table carrying its own row predicates (applied before joins).
        var from = new StringBuilder();
        from.Append("FROM ").Append(Source(plan.Objects.Single(o => string.Equals(o.Alias, query.Source.Alias, StringComparison.OrdinalIgnoreCase)), context));
        foreach (var join in query.Joins)
        {
            var target = objects[join.Alias];
            var on = string.Join(" AND ", join.On.Select(c => $"{Column(c.Left)} = {Column(c.Right)}"));
            from.Append(' ').Append(join.Type.Equals("left", StringComparison.OrdinalIgnoreCase) ? "LEFT JOIN " : "INNER JOIN ").Append(Source(target, context)).Append(" ON ").Append(on);
        }

        // WHERE
        var where = query.Filters is null ? null : Group(query.Filters, Column, ColumnType, context);

        // GROUP BY / ORDER BY
        var groupBy = query.GroupBy.Select(g => aliasExpressions[g]).ToList();
        var orderBy = query.OrderBy.Select(o => $"{Quote(o.Alias)} {(o.Direction.Equals("desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC")}").ToList();

        var sql = new StringBuilder();
        sql.Append("SELECT ").Append(string.Join(", ", select)).Append(' ').Append(from);
        if (!string.IsNullOrEmpty(where))
        {
            sql.Append(" WHERE ").Append(where);
        }

        if (groupBy.Count > 0)
        {
            sql.Append(" GROUP BY ").Append(string.Join(", ", groupBy));
        }

        if (orderBy.Count > 0)
        {
            sql.Append(" ORDER BY ").Append(string.Join(", ", orderBy));
        }

        // One extra row lets the executor report truncation without reading past the cap.
        var rowCap = plan.EffectiveLimit;
        var text = ApplyLimit(sql.ToString(), rowCap + 1, orderBy.Count > 0);
        Guard(text);
        return new CompiledQuery(text, context.Parameters, Redact(text, context), rowCap);
    }

    private string Source(AuthorizedObject o, CompileContext context)
    {
        var table = $"{Quote(o.Schema)}.{Quote(o.ObjectName)}";
        if (o.RowPredicates.Count == 0)
        {
            return $"{table} {Quote(o.Alias)}";
        }

        var predicates = o.RowPredicates.Select(p =>
        {
            var columnName = o.ColumnTypes.Keys.FirstOrDefault(k => string.Equals(k, p.Column, StringComparison.OrdinalIgnoreCase)) ?? p.Column;
            object? value = p.Value;
            if (o.ColumnTypes.TryGetValue(columnName, out var type))
            {
                try
                {
                    value = Convert(p.Value, type);
                }
                catch (DomainException)
                {
                    // The mapping column cannot hold the user's context value (e.g. a GUID column mapped to a department code).
                    throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "row_mapping_type_mismatch");
                }
            }

            return $"{Quote(columnName)} = {context.Add(value)}";
        });
        return $"(SELECT * FROM {table} WHERE {string.Join(" OR ", predicates)}) {Quote(o.Alias)}";
    }

    private string Group(PlanFilterGroup group, Func<string, string> column, Func<string, string> columnType, CompileContext context)
    {
        var parts = new List<string>();
        foreach (var condition in group.Conditions)
        {
            parts.Add(Condition(condition, column(condition.Column), columnType(condition.Column), context));
        }

        foreach (var child in group.Groups)
        {
            var nested = Group(child, column, columnType, context);
            if (!string.IsNullOrEmpty(nested))
            {
                parts.Add($"({nested})");
            }
        }

        return string.Join(group.Logic.Equals("or", StringComparison.OrdinalIgnoreCase) ? " OR " : " AND ", parts);
    }

    private string Condition(PlanCondition condition, string column, string dataType, CompileContext context)
    {
        var op = condition.Operator.ToLowerInvariant();
        object? Typed(string v) => Convert(v, dataType);
        return op switch
        {
            "eq" => $"{column} = {context.Add(Typed(condition.Values[0]))}",
            "neq" => $"{column} <> {context.Add(Typed(condition.Values[0]))}",
            "gt" => $"{column} > {context.Add(Typed(condition.Values[0]))}",
            "gte" => $"{column} >= {context.Add(Typed(condition.Values[0]))}",
            "lt" => $"{column} < {context.Add(Typed(condition.Values[0]))}",
            "lte" => $"{column} <= {context.Add(Typed(condition.Values[0]))}",
            "in" => $"{column} IN ({string.Join(", ", condition.Values.Select(v => context.Add(Typed(v))))})",
            "not_in" => $"{column} NOT IN ({string.Join(", ", condition.Values.Select(v => context.Add(Typed(v))))})",
            "between" => $"{column} BETWEEN {context.Add(Typed(condition.Values[0]))} AND {context.Add(Typed(condition.Values[1]))}",
            "contains" => $"{column} LIKE {context.Add("%" + Escape(condition.Values[0]) + "%")} {EscapeClause}",
            "starts_with" => $"{column} LIKE {context.Add(Escape(condition.Values[0]) + "%")} {EscapeClause}",
            "is_null" => $"{column} IS NULL",
            "not_null" => $"{column} IS NOT NULL",
            _ => throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "plan_operator_invalid"),
        };
    }

    protected virtual string EscapeClause => "ESCAPE '\\'";

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>Typed parameters keep the column side of each predicate untouched (sargable) and reject values that do not fit the column.</summary>
    internal static object? Convert(string value, string dataType)
    {
        var type = dataType.ToLowerInvariant();
        try
        {
            if (BooleanTypes.Contains(type))
            {
                return value is "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
            }

            if (type == "uniqueidentifier")
            {
                return Guid.Parse(value);
            }

            if (DateTypes.Any(t => type.StartsWith(t, StringComparison.Ordinal)))
            {
                return DateTime.SpecifyKind(DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), DateTimeKind.Unspecified);
            }

            if (IntegerTypes.Contains(type) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            {
                return l;
            }

            if (DecimalTypes.Any(t => type.StartsWith(t, StringComparison.Ordinal)))
            {
                return decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            return value;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "plan_filter_value_type_mismatch");
        }
    }

    /// <summary>Last line of defence: a single SELECT statement, no batch separators or comments.</summary>
    private static void Guard(string sql)
    {
        if (!sql.StartsWith("SELECT ", StringComparison.Ordinal) || sql.Contains(';', StringComparison.Ordinal) || sql.Contains("--", StringComparison.Ordinal) || sql.Contains("/*", StringComparison.Ordinal))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "compiled_sql_rejected");
        }
    }

    private static string Redact(string sql, CompileContext context)
    {
        var preview = sql;
        foreach (var parameter in context.Parameters.OrderByDescending(p => p.Name.Length))
        {
            preview = preview.Replace(parameter.Name, "?", StringComparison.Ordinal);
        }

        return preview;
    }

    protected sealed class CompileContext
    {
        private readonly string _prefix;
        private readonly List<SqlParameter> _parameters = [];

        public CompileContext(string prefix)
        {
            _prefix = prefix;
        }

        public IReadOnlyList<SqlParameter> Parameters => _parameters;

        public string Add(object? value)
        {
            var name = $"{_prefix}p{_parameters.Count}";
            _parameters.Add(new SqlParameter(name, value));
            return name;
        }
    }
}

public sealed class SqlServerCompiler : SqlCompilerBase
{
    public override DatabaseType DatabaseType => DatabaseType.SqlServer;

    protected override string ParameterPrefix => "@";

    protected override string Quote(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    protected override string DateTrunc(string unit, string column) => unit switch
    {
        "day" => $"CAST({column} AS date)",
        "week" => $"DATEADD(day, -((DATEPART(weekday, {column}) + @@DATEFIRST - 2) % 7), CAST({column} AS date))",
        "month" => $"DATEFROMPARTS(YEAR({column}), MONTH({column}), 1)",
        "quarter" => $"DATEFROMPARTS(YEAR({column}), ((MONTH({column}) - 1) / 3) * 3 + 1, 1)",
        "year" => $"DATEFROMPARTS(YEAR({column}), 1, 1)",
        _ => throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "plan_date_trunc_invalid"),
    };

    protected override string ApplyLimit(string selectWithoutLimit, int limit, bool hasOrderBy)
        => "SELECT TOP (" + limit.ToString(CultureInfo.InvariantCulture) + ") " + selectWithoutLimit["SELECT ".Length..];
}

public sealed class OracleCompiler : SqlCompilerBase
{
    public override DatabaseType DatabaseType => DatabaseType.Oracle;

    protected override string ParameterPrefix => ":";

    protected override string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    protected override string DateTrunc(string unit, string column) => unit switch
    {
        "day" => $"TRUNC({column})",
        "week" => $"TRUNC({column}, 'IW')",
        "month" => $"TRUNC({column}, 'MM')",
        "quarter" => $"TRUNC({column}, 'Q')",
        "year" => $"TRUNC({column}, 'YYYY')",
        _ => throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "plan_date_trunc_invalid"),
    };

    protected override string ApplyLimit(string selectWithoutLimit, int limit, bool hasOrderBy)
        => selectWithoutLimit + " FETCH FIRST " + limit.ToString(CultureInfo.InvariantCulture) + " ROWS ONLY";
}

public sealed class SqlDialectCompilerFactory : ISqlDialectCompilerFactory
{
    private readonly Dictionary<DatabaseType, ISqlDialectCompiler> _compilers = new()
    {
        [DatabaseType.SqlServer] = new SqlServerCompiler(),
        [DatabaseType.Oracle] = new OracleCompiler(),
        // Platform Insights views live in the SQL Server configuration database.
        [DatabaseType.PlatformInsights] = new SqlServerCompiler(),
    };

    public ISqlDialectCompiler Resolve(DatabaseType databaseType)
        => _compilers.TryGetValue(databaseType, out var compiler) ? compiler : throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "database_type_unsupported");
}
