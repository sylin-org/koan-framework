namespace Koan.Data.Abstractions;

public interface IStringQueryRepository<TEntity, TKey> where TEntity : IEntity<TKey>
{
    Task<IReadOnlyList<TEntity>> Query(string query, CancellationToken ct = default);
}
