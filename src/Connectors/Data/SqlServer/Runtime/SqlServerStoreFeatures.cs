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

    public bool SupportsMappedIndexes => true;

    /// <summary>
    /// The index sits on the persisted column this store computes from the document, and reads resolve through
    /// that same column, so a query is served by the index without naming it.
    /// </summary>
    public bool SupportsRewriteFreeExpressionIndexes => true;

    public bool SupportsNativeTtl => false;

    /// <summary>
    /// A mapped scalar is read out of the document as <c>nvarchar(4000)</c> unless the dialect casts it, and a
    /// nonclustered key entry may not exceed 1700 bytes. Types the dialect casts to <c>bit</c>, <c>bigint</c> or
    /// <c>decimal</c> are keys of fixed width; a date or a Guid stays text but its encoding is bounded well
    /// under the limit. Free text and binary are neither, so they are declined rather than indexed into a store
    /// that will reject the first long value written to it - measured, not assumed: a 2000-character label
    /// failed the insert with "index entry of length 4000 bytes exceeds the maximum length of 1700 bytes".
    /// </summary>
    public bool CanIndexKey(Type physicalType)
    {
        var value = Nullable.GetUnderlyingType(physicalType) ?? physicalType;
        return value != typeof(string) && value != typeof(byte[]);
    }
}
