using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.Sqlite.Runtime;

/// <summary>What SQLite can hold, so the schema owner knows what to ask of it.</summary>
internal sealed class SqliteStoreFeatures : IRelationalStoreFeatures
{
    internal static readonly SqliteStoreFeatures Instance = new();

    private SqliteStoreFeatures() { }

    public string ProviderName => "sqlite";

    public bool SupportsJsonFunctions => true;

    /// <summary>
    /// SQLite has held generated columns since 3.31, but Koan has never built one here: its expression indexes
    /// read the document directly and the planner uses them, so a materialized copy of every scalar would cost
    /// write throughput and storage for nothing. Turning this on is a one-line decision the day that changes.
    /// </summary>
    public bool SupportsPersistedComputedColumns => false;

    public bool SupportsMappedIndexes => true;

    /// <summary>An index over <c>json_extract</c> is chosen by SQLite for the reads that spell it the same way.</summary>
    public bool SupportsRewriteFreeExpressionIndexes => true;

    public bool SupportsNativeTtl => false;
}
