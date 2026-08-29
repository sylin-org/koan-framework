using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.Firebird.Runtime;

/// <summary>What Firebird can hold, so the schema owner knows what to ask of it.</summary>
internal sealed class FirebirdStoreFeatures : IRelationalStoreFeatures
{
    internal static readonly FirebirdStoreFeatures Instance = new();

    private FirebirdStoreFeatures() { }

    public string ProviderName => Infrastructure.Constants.Provider;

    /// <summary>Firebird 5 ships no JSON functions; structured values are opaque text to its planner.</summary>
    public bool SupportsJsonFunctions => false;

    public bool SupportsPersistedComputedColumns => false;

    /// <summary>Flat physical columns index directly; nested parts have no column and no expression route.</summary>
    public bool SupportsMappedIndexes => true;

    public bool SupportsRewriteFreeExpressionIndexes => false;

    public bool SupportsNativeTtl => false;
}
