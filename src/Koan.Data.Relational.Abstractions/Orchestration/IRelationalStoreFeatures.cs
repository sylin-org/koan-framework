namespace Koan.Data.Relational.Orchestration;

/// <summary>
/// What one store can do, so the schema owner can decide what to ask of it.
///
/// <para>Each answer is a capability, never a preference. The framework decides whether a projected column or a
/// mapped index is worth having; a store only says whether it can hold one. That is what keeps four adapters
/// from arriving at four different answers to the same question (DATA-0119, ARCH-0084).</para>
/// </summary>
public interface IRelationalStoreFeatures
{
    /// <summary>How this store is named in a refusal or a health report.</summary>
    string ProviderName { get; }

    /// <summary>Whether the store can read a value out of a structured column at all.</summary>
    bool SupportsJsonFunctions { get; }

    /// <summary>
    /// Whether the store can hold a column it computes from the structured root on every write, so that
    /// filtering and ordering on a mapped value reach a materialized column rather than a document read.
    /// </summary>
    bool SupportsPersistedComputedColumns { get; }

    /// <summary>Whether the store can build the mapping's declared non-primary indexes.</summary>
    bool SupportsMappedIndexes => false;

    /// <summary>
    /// Whether an index over a value read out of the structured root is actually chosen by the planner for the
    /// reads that value serves, without the query being rewritten to name the index.
    /// </summary>
    bool SupportsRewriteFreeExpressionIndexes => false;

    /// <summary>
    /// Whether an index part can read a framework-managed field (the tenant) inside the structured root directly,
    /// which is what lets a unique index lead with the tenant (DATA-0150). A store that indexes a document value only
    /// through a projected column it computes per mapped property has no column for a managed field, so it keeps the
    /// global unique index and reports the scoping as unproved.
    /// </summary>
    bool SupportsManagedFieldIndexParts => false;

    /// <summary>Whether the store expires rows itself, so a TTL index means something here.</summary>
    bool SupportsNativeTtl => false;

    /// <summary>
    /// Whether the store keeps a lexical index over an entity's declared text, in step with every write of the row
    /// inside the same write unit, and answers a ranked term search over it. A store that answers true also declares
    /// <c>query.lexical</c> on its repository and spells the index through
    /// <see cref="IRelationalDdlExecutor.CreateLexicalIndex"/>.
    /// </summary>
    bool SupportsLexicalIndex => false;

    /// <summary>
    /// Whether a value of this type can serve as an index key here.
    ///
    /// <para>Some stores cap what a key may hold. SQL Server reads a mapped scalar out of the document as
    /// <c>nvarchar(4000)</c> and refuses an index entry over 1700 bytes, so indexing a text property produces an
    /// index that accepts short rows and rejects long ones - a write that fails in production and never in a
    /// test. A store that cannot key a value says so, the claim is recorded as unproved, and no index is built;
    /// silently building one that breaks writes is the worse of the two failures.</para>
    /// </summary>
    bool CanIndexKey(Type physicalType) => true;
}
