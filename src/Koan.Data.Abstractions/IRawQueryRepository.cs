using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Abstractions;

/// <summary>
/// Optional escape hatch for provider-native query strings (raw SQL, N1QL, free-text search),
/// gated behind adapter support (DATA-0096 section 7.5). Distinct from the structured
/// <see cref="IQueryRepository{TEntity, TKey}"/> path — a raw query is never reinterpreted as a
/// <see cref="Filter"/>, and adapters that don't implement this simply don't offer the hatch
/// (callers get a clear NotSupported rather than a silent match-all).
/// <para>The query text belongs to the caller: application code, never request text. The HTTP controller forwards
/// <c>?q=</c> here only when it declared <c>AllowRawQuery</c>, and the MCP edge never does (WEB-0074). SQLite and
/// PostgreSQL confine the text to a single read-only statement, CockroachDB to a single statement (its read-only mode
/// refuses DML, not DDL); the other relational adapters run it as written.</para>
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
