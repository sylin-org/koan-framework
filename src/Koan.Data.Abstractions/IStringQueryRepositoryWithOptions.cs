using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Data.Abstractions;

public interface IStringQueryRepositoryWithOptions<TEntity, TKey> : IStringQueryRepository<TEntity, TKey>
    where TEntity : IEntity<TKey>
    where TKey : notnull
{
    new Task<IReadOnlyList<TEntity>> Query(string query, DataQueryOptions? options, CancellationToken ct = default);
    Task<IReadOnlyList<TEntity>> Query(string query, object? parameters, DataQueryOptions? options, CancellationToken ct = default);
}
