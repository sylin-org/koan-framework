using System.Linq.Expressions;

namespace Sora.Data.Abstractions;

public interface ILinqQueryRepository<TEntity, TKey> : IDataRepository<TEntity, TKey>
    where TEntity : IEntity<TKey>
    where TKey : notnull
{
    Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default);
    Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default);
}