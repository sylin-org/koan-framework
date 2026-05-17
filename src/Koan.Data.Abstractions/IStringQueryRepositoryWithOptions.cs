namespace Koan.Data.Abstractions;

public interface IStringQueryRepositoryWithOptions<TEntity, TKey> : IStringQueryRepository<TEntity, TKey>
    where TEntity : IEntity<TKey>
    where TKey : notnull
{
    /// <summary>
    /// String-query with sort/page/filter hints. Returns a <see cref="RepositoryQueryResult{TEntity}"/>
    /// declaring which capabilities the adapter pushed down.
    /// </summary>
    Task<RepositoryQueryResult<TEntity>> Query(string query, DataQueryOptions? options, CancellationToken ct = default);

    /// <summary>Parameterised variant of the above.</summary>
    Task<RepositoryQueryResult<TEntity>> Query(string query, object? parameters, DataQueryOptions? options, CancellationToken ct = default);
}
