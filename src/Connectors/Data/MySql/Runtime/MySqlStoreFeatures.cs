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

    /// <summary>
    /// Koan has never built a declared index on this store. Leaving it false is what makes an entity's
    /// <c>[Index]</c> surface as unproved rather than disappear.
    /// </summary>
    public bool SupportsMappedIndexes => false;

    public bool SupportsRewriteFreeExpressionIndexes => false;

    public bool SupportsNativeTtl => false;
}
