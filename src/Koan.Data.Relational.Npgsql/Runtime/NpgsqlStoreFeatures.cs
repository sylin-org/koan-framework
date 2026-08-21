using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Relational.Npgsql.Runtime;

/// <summary>What a PostgreSQL-wire store can hold, so the schema owner knows what to ask of it.</summary>
/// <param name="providerName">
/// Named per route rather than fixed, because this runtime serves both PostgreSQL and CockroachDB and a refusal
/// has to say which one refused.
/// </param>
internal sealed class NpgsqlStoreFeatures(string providerName) : IRelationalStoreFeatures
{
    public string ProviderName => providerName;

    public bool SupportsJsonFunctions => true;

    /// <summary>
    /// PostgreSQL has held generated columns since 12, but Koan has never built one here. Turning this on adds a
    /// materialized column per scalar property and needs its own proof that the planner then uses it.
    /// </summary>
    public bool SupportsPersistedComputedColumns => false;

    public bool SupportsMappedIndexes => true;

    /// <summary>
    /// An index over <c>#&gt;&gt;</c> into the document column is chosen by the planner for reads that spell the
    /// value the same way, without the query naming the index. Every cast the dialect emits is immutable, which
    /// is the precondition for indexing the expression at all.
    /// </summary>
    public bool SupportsRewriteFreeExpressionIndexes => true;

    public bool SupportsNativeTtl => false;
}
