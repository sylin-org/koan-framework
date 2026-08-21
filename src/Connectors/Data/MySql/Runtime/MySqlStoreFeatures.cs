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

    /// <summary>
    /// A mapped scalar the dialect does not cast is held as <c>longtext</c>, and MySQL refuses a key over a
    /// TEXT or BLOB column without a prefix length. Numbers, dates and Guids get bounded column types and index
    /// cleanly; free text and binary do not, and are declined rather than turned into a prefix index whose
    /// selectivity nobody asked for.
    /// </summary>
    public bool CanIndexKey(Type physicalType)
    {
        var value = Nullable.GetUnderlyingType(physicalType) ?? physicalType;
        return value != typeof(string) && value != typeof(byte[]);
    }
}
