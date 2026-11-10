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

    /// <summary>
    /// The operand of SUM or AVG over a non-boolean column. Dialects whose integer arithmetic would give a wrong answer
    /// (an average cut to a whole number, a total that overflows) cast it here.
    /// </summary>
    protected virtual string AggregateOperand(string aggregate, string column, string dataType) => column;

    /// <summary>True when a column of this date type also stores a time of day (so "on that day" is a range, not an equality).</summary>
    protected virtual bool HasTimeOfDay(string dataType) => !string.Equals(dataType, "date", StringComparison.OrdinalIgnoreCase);

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

        // SUM and AVG: bit columns as above; other columns as the dialect needs (whole-number columns in SQL Server).
        string Operand(string reference, string aggregate, string booleanTarget)
        {
            var type = ColumnType(reference).ToLowerInvariant();
            return BooleanTypes.Contains(type) ? $"CAST({Column(reference)} AS {booleanTarget})" : AggregateOperand(aggregate, Column(reference), type);
        }

        // A placeholder becomes its value for this run (resolved by the authorizer): the asker's own code, or the date a
        // relative date stands for today. Anything else is a literal.
        string Value(string value)
        {
            if (!AskerValues.IsPlaceholder(value) && !RelativeDates.IsPlaceholder(value))
            {
                return value;
            }

            return plan.AskerValues?.GetValueOrDefault(value) ?? throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "asker_value_missing");
        }

        var expressions = new Expressions(Column, ColumnType, Value);

        // SELECT
        var select = new List<string>();
        var aliasExpressions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var aliasTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                    "sum" => $"SUM({Operand(item.Column!, "sum", "INT")})",
                    "avg" => $"AVG({Operand(item.Column!, "avg", "FLOAT")})",
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
            if (item.Aggregate is not null)
            {
                // The type a HAVING value is converted to: counts are integers, sums and averages decimals, MIN/MAX keep the column's type.
                aliasTypes[item.Alias] = item.Aggregate.ToLowerInvariant() switch
                {
                    "count" or "count_distinct" => "bigint",
                    "sum" or "avg" => "decimal",
                    _ => BooleanTypes.Contains(ColumnType(item.Column!).ToLowerInvariant()) ? "int" : ColumnType(item.Column!),
                };
            }
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
        var where = query.Filters is null ? null : Group(query.Filters, expressions, context);

        // HAVING: conditions on aggregate select items, compiled against their expressions (aliases are not visible there).
        var having = query.Having.Count == 0
            ? null
            : Group(new PlanFilterGroup { Logic = "and", Conditions = query.Having }, new Expressions(a => aliasExpressions[a], a => aliasTypes[a], v => v), context);

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

        if (!string.IsNullOrEmpty(having))
        {
            sql.Append(" HAVING ").Append(having);
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

    /// <summary>How a filter group resolves its references: SQL expression, data type and value (placeholders resolved).</summary>
    private sealed record Expressions(Func<string, string> Column, Func<string, string> ColumnType, Func<string, string> Value);

    private string Group(PlanFilterGroup group, Expressions expressions, CompileContext context)
    {
        var parts = new List<string>();
        foreach (var condition in group.Conditions)
        {
            parts.Add(condition.CompareTo is null
                ? Condition(condition, expressions.Column(condition.Column), expressions.ColumnType(condition.Column), expressions.Value, context)
                : Comparison(condition, expressions));
        }

        foreach (var child in group.Groups)
        {
            var nested = Group(child, expressions, context);
            if (!string.IsNullOrEmpty(nested))
            {
                parts.Add($"({nested})");
            }
        }

        return string.Join(group.Logic.Equals("or", StringComparison.OrdinalIgnoreCase) ? " OR " : " AND ", parts);
    }

    /// <summary>Column against column (or aggregate against aggregate): both sides must hold the same kind of value.</summary>
    private static string Comparison(PlanCondition condition, Expressions expressions)
    {
        // Both sides resolve first, so a column outside the authorized plan fails as not permitted.
        var left = expressions.Column(condition.Column);
        var right = expressions.Column(condition.CompareTo!);
        if (Family(expressions.ColumnType(condition.Column)) != Family(expressions.ColumnType(condition.CompareTo!)))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "plan_compare_type_mismatch");
        }

        var op = condition.Operator.ToLowerInvariant() switch
        {
            "eq" => "=",
            "neq" => "<>",
            "gt" => ">",
            "gte" => ">=",
            "lt" => "<",
            "lte" => "<=",
            _ => throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "plan_compare_invalid"),
        };
        return $"{left} {op} {right}";
    }

    /// <summary>Kind of value a data type holds, for column-to-column comparisons.</summary>
    internal static string Family(string dataType)
    {
        var type = dataType.ToLowerInvariant();
        if (DateTypes.Any(t => type.StartsWith(t, StringComparison.Ordinal)))
        {
            return "date";
        }

        if (IntegerTypes.Contains(type) || DecimalTypes.Any(t => type.StartsWith(t, StringComparison.Ordinal)))
        {
            return "number";
        }

        return BooleanTypes.Contains(type) ? "boolean" : type == "uniqueidentifier" ? "guid" : "text";
    }

    private string Condition(PlanCondition condition, string column, string dataType, Func<string, string> value, CompileContext context)
    {
        var op = condition.Operator.ToLowerInvariant();
        var isDate = Family(dataType) == "date";
        if (!isDate && condition.Values.Any(RelativeDates.IsPlaceholder))
        {
            // "$month_start" against a text or number column would silently compare with a date written as text.
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "plan_filter_value_type_mismatch");
        }

        object? Typed(string v) => Convert(value(v), dataType);
        string P(string v) => context.Add(Typed(v));

        // A value given as a date only (no time) means that whole day. On a column that also stores a time of day, "equal to",
        // "up to" and "after" that day are therefore ranges ending before the next day starts: a plain comparison would stop at
        // midnight and drop the rest of the day.
        var wholeDays = isDate && HasTimeOfDay(dataType);
        bool WholeDay(string v) => wholeDays && NextDay(value(v)) is not null;
        string After(string v) => context.Add(Convert(NextDay(value(v))!, dataType));
        string On(string v) => WholeDay(v) ? $"({column} >= {P(v)} AND {column} < {After(v)})" : $"{column} = {P(v)}";

        return op switch
        {
            "eq" => On(condition.Values[0]),
            "neq" => WholeDay(condition.Values[0]) ? $"({column} < {P(condition.Values[0])} OR {column} >= {After(condition.Values[0])})" : $"{column} <> {P(condition.Values[0])}",
            "gt" => WholeDay(condition.Values[0]) ? $"{column} >= {After(condition.Values[0])}" : $"{column} > {P(condition.Values[0])}",
            "gte" => $"{column} >= {P(condition.Values[0])}",
            "lt" => $"{column} < {P(condition.Values[0])}",
            "lte" => WholeDay(condition.Values[0]) ? $"{column} < {After(condition.Values[0])}" : $"{column} <= {P(condition.Values[0])}",
            "in" when condition.Values.Any(WholeDay) => $"({string.Join(" OR ", condition.Values.Select(On))})",
            "not_in" when condition.Values.Any(WholeDay) => $"NOT ({string.Join(" OR ", condition.Values.Select(On))})",
            "in" => $"{column} IN ({string.Join(", ", condition.Values.Select(P))})",
            "not_in" => $"{column} NOT IN ({string.Join(", ", condition.Values.Select(P))})",
            "between" when WholeDay(condition.Values[1]) => $"({column} >= {P(condition.Values[0])} AND {column} < {After(condition.Values[1])})",
            "between" => $"{column} BETWEEN {P(condition.Values[0])} AND {P(condition.Values[1])}",
            "contains" => $"{column} LIKE {context.Add("%" + Escape(condition.Values[0]) + "%")} {EscapeClause}",
            "starts_with" => $"{column} LIKE {context.Add(Escape(condition.Values[0]) + "%")} {EscapeClause}",
            "is_null" => $"{column} IS NULL",
            "not_null" => $"{column} IS NOT NULL",
            _ => throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "plan_operator_invalid"),
        };
    }

    /// <summary>The day after a value written as a date only (YYYY-MM-DD); null when the value carries a time or is not a date.</summary>
    private static string? NextDay(string value)
        => value.Length == 10 && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day < DateOnly.MaxValue
            ? day.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;

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

            if (IntegerTypes.Contains(type))
            {
                // A whole number stays an integer; "2.5" against an integer column is still a number; "two" is rejected.
                return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? (object)l : decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
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

    // T-SQL keeps the operand's type: AVG over a whole-number column is cut to a whole number (2, 3, 3 gives 2), and SUM over
    // int overflows above 2,147,483,647. Average as float and total as bigint instead.
    protected override string AggregateOperand(string aggregate, string column, string dataType) => (aggregate, dataType) switch
    {
        ("avg", "int" or "bigint" or "smallint" or "tinyint") => $"CAST({column} AS FLOAT)",
        ("sum", "int" or "smallint" or "tinyint") => $"CAST({column} AS BIGINT)",
        _ => column,
    };

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

    // An Oracle DATE stores a time of day too.
    protected override bool HasTimeOfDay(string dataType) => true;

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
