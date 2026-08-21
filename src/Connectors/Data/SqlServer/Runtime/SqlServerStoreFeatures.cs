using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.SqlServer.Runtime;

/// <summary>What SQL Server can hold, so the schema owner knows what to ask of it.</summary>
internal sealed class SqlServerStoreFeatures : IRelationalStoreFeatures
{
    internal static readonly SqlServerStoreFeatures Instance = new();

    private SqlServerStoreFeatures() { }

    public string ProviderName => Infrastructure.Constants.Provider;

    public bool SupportsJsonFunctions => true;

    /// <summary>
    /// SQL Server holds a <c>PERSISTED</c> computed column per mapped scalar, and its optimizer substitutes one
    /// for the matching <c>JSON_VALUE</c> expression without the query naming it.
    /// </summary>
    public bool SupportsPersistedComputedColumns => true;

    /// <summary>
    /// Koan has never built a declared index on this store. Leaving it false is what makes an entity's
    /// <c>[Index]</c> surface as unproved rather than disappear.
    /// </summary>
    public bool SupportsMappedIndexes => false;

    public bool SupportsRewriteFreeExpressionIndexes => false;

    public bool SupportsNativeTtl => false;
}
