namespace Sora.Data.Abstractions;

public interface IDataRepositoryWithOptions<TEntity, TKey> : IDataRepository<TEntity, TKey>
    where TEntity : IEntity<TKey>
    where TKey : notnull
{
    Task<IReadOnlyList<TEntity>> QueryAsync(object? query, DataQueryOptions? options, CancellationToken ct = default);
}