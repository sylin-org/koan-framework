using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.MySql.Runtime;

/// <summary>What MySQL can hold, so the schema owner knows what to ask of it.</summary>
internal sealed class MySqlStoreFeatures : IRelationalStoreFeatures
{
    internal static readonly MySqlStoreFeatures Instance = new();

    private MySqlStoreFeatures() { }

    public string ProviderName => Infrastructure.Constants.Provider;

    public bool SupportsJsonFunctions => true;

    /// <summary>MySQL holds a <c>STORED</c> generated column per mapped scalar, computed on every write.</summary>
    public bool SupportsPersistedComputedColumns => true;

    public bool SupportsMappedIndexes => true;

    /// <summary>
    /// The index sits on the persisted column this store computes from the document, and reads resolve through
    /// that same column, so a query is served by the index without naming it.
    /// </summary>
    public bool SupportsRewriteFreeExpressionIndexes => true;

    public bool SupportsNativeTtl => false;

    // Every mapped type can be a key here. Numbers, dates and Guids get bounded column types; text and binary
    // are held as longtext or longblob and take a prefix, which MySQL answers by seeking the prefix and then
    // rechecking the full column, so the index stays exact rather than approximate.
}
