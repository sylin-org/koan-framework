using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Abstractions;

/// <summary>
/// Optional escape hatch for provider-native query strings (raw SQL, N1QL, free-text search),
/// gated behind adapter support (DATA-XXXX decision #5). Distinct from the structured
/// <see cref="IQueryRepository{TEntity, TKey}"/> path — a raw query is never reinterpreted as a
/// <see cref="Filter"/>, and adapters that don't implement this simply don't offer the hatch
/// (callers get a clear NotSupported rather than a silent match-all).
/// </summary>
public interface IRawQueryRepository<TEntity, TKey>
    where TEntity : IEntity<TKey>
    where TKey : notnull
{
    /// <summary>Run a provider-native query. <paramref name="shaping"/> supplies Page/PageSize/Partition (its Filter is ignored).</summary>
    Task<RepositoryQueryResult<TEntity>> QueryRaw(string query, object? parameters, QueryDefinition shaping, CancellationToken ct = default);

    /// <summary>Count matching rows for a provider-native query.</summary>
    Task<CountResult> CountRaw(string query, object? parameters, CancellationToken ct = default);
}
