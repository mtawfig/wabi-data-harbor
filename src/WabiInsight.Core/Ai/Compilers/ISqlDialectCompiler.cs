using WabiInsight.Core.Entities.Organizations;

namespace WabiInsight.Core.Ai.Compilers;

public sealed record SqlParameter(string Name, object? Value);

/// <summary>Parameterized, read-only SQL plus a preview with parameter values redacted (for history and the UI).</summary>
public sealed record CompiledQuery(string Sql, IReadOnlyList<SqlParameter> Parameters, string RedactedPreview, int RowCap);

/// <summary>
/// Emits SQL from an authorized plan (specification §19 step 11). Row predicates are applied inside a derived
/// table per object, before joins and aggregations; every literal becomes a parameter; identifiers come
/// from the catalog and are quoted. The compiler never sees free text from the model.
/// </summary>
public interface ISqlDialectCompiler
{
    DatabaseType DatabaseType { get; }

    CompiledQuery Compile(AuthorizedPlan plan);

    /// <summary>The same query with its order and without a row limit: every row of the list, for the Excel export (P13.50).</summary>
    CompiledQuery CompileAll(AuthorizedPlan plan);

    /// <summary>
    /// One row about the whole list of a plan: its number of rows (<c>wi_rows</c>) and, for the i-th of
    /// <paramref name="numericColumns"/>, its total, average, lowest and highest (<c>s{i}</c>, <c>a{i}</c>, <c>n{i}</c>, <c>x{i}</c>).
    /// The plan's own query, with the same row predicates and parameters, is the source; nothing else is read.
    /// </summary>
    CompiledQuery CompileSummary(AuthorizedPlan plan, IReadOnlyList<string> numericColumns);
}

public interface ISqlDialectCompilerFactory
{
    ISqlDialectCompiler Resolve(DatabaseType databaseType);
}
