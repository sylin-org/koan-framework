using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Data.Abstractions;

public interface IPagedRepository<TEntity, TKey> where TEntity : IEntity<TKey>
{
    Task<PagedRepositoryResult<TEntity>> QueryPageAsync(
        object? query,
        DataQueryOptions options,
        CancellationToken ct = default);
}

public sealed class PagedRepositoryResult<TEntity>
{
    public required IReadOnlyList<TEntity> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
}
