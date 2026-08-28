using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.DuckDb.Runtime;

/// <summary>What DuckDB can hold, so the schema owner knows what to ask of it.</summary>
internal sealed class DuckDbStoreFeatures : IRelationalStoreFeatures
{
    internal static readonly DuckDbStoreFeatures Instance = new();

    private DuckDbStoreFeatures() { }

    public string ProviderName => "duckdb";

    public bool SupportsJsonFunctions => true;

    /// <summary>
    /// DuckDB supports generated columns, but the adapter has never built one: expression reads go through
    /// the document directly and no workload here has asked for a materialized scalar copy. Turning this on
    /// is a one-line decision the day that changes.
    /// </summary>
    public bool SupportsPersistedComputedColumns => false;

    public bool SupportsMappedIndexes => true;

    /// <summary>
    /// Deliberately false while unproven: SQLite's planner picks an expression index on exact textual match
    /// with the query's read expression, and ANL-1 has not demonstrated the same match behavior for DuckDB's
    /// planner. Declaring it would let the orchestrator depend on an index the engine may ignore.
    /// </summary>
    public bool SupportsRewriteFreeExpressionIndexes => false;

    public bool SupportsNativeTtl => false;
}
