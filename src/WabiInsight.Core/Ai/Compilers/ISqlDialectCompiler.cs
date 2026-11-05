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
}

public interface ISqlDialectCompilerFactory
{
    ISqlDialectCompiler Resolve(DatabaseType databaseType);
}
