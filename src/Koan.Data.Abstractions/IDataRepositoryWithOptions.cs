namespace Koan.Data.Abstractions;

public interface IDataRepositoryWithOptions<TEntity, TKey> : IDataRepository<TEntity, TKey>
    where TEntity : IEntity<TKey>
    where TKey : notnull
{
    /// <summary>
    /// Query with sort/page/filter hints. Returns a <see cref="RepositoryQueryResult{TEntity}"/> declaring
    /// which capabilities the adapter pushed down. The orchestrator inspects the result and falls back
    /// to in-memory sort/page when the adapter could not handle the request fully.
    /// </summary>
    Task<RepositoryQueryResult<TEntity>> Query(object? query, DataQueryOptions? options, CancellationToken ct = default);
}
