namespace Sora.Data.Abstractions;

public interface IStringQueryRepository<TEntity, TKey> : IDataRepository<TEntity, TKey>
    where TEntity : IEntity<TKey>
    where TKey : notnull
{
    Task<IReadOnlyList<TEntity>> QueryAsync(string query, CancellationToken ct = default);
    // Optional overload to supply parameters for safe binding
    Task<IReadOnlyList<TEntity>> QueryAsync(string query, object? parameters, CancellationToken ct = default);
    Task<int> CountAsync(string query, CancellationToken ct = default);
    Task<int> CountAsync(string query, object? parameters, CancellationToken ct = default);
}