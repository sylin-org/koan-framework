using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Sorting;

namespace Koan.Data.Core.Querying;

/// <summary>The finalized page of a query plus its true total cardinality.</summary>
public readonly record struct FinalizedQuery<TEntity>(
    IReadOnlyList<TEntity> Page,
    long TotalCount,
    bool IsEstimate,
    bool FellBackInMemory);

/// <summary>
/// The single owner of the partial-pushdown algorithm. An adapter is a translator + executor; this
/// coordinator is the orchestrator. It splits the caller's filter against the adapter's declared
/// <see cref="FilterSupport"/>, invokes the adapter with only the pushable portion, then applies
/// — in the only correctness-safe order — the residual filter, the unhandled sort, and pagination.
/// Pagination is applied <b>after</b> the residual, which structurally eliminates the relational
/// mis-pagination bug (old adapters paginated the unfiltered set, then filtered). Because this lives
/// in exactly one place, no adapter carries fallback logic.
/// </summary>
public static class FilterPushdownCoordinator
{
    /// <summary>
    /// Plan the adapter call for a query: the pushable-only definition to hand the adapter, and the
    /// residual the coordinator must evaluate afterwards. When a residual exists, pagination is stripped
    /// from the adapter definition (the page must be taken after the residual filter).
    /// </summary>
    public static (QueryDefinition AdapterQuery, Filter? Residual) Plan(QueryDefinition query, FilterSupport caps, Type entityType)
    {
        if (query.Filter is null)
            return (query, null);

        var split = FilterSplitter.Split(query.Filter, caps, entityType);
        var adapterQuery = query.Where(split.Pushable);
        if (split.Residual is not null)
            adapterQuery = adapterQuery.WithoutPagination();
        return (adapterQuery, split.Residual);
    }

    /// <summary>
    /// Finalize an adapter result against the original query and the planned residual: apply the residual
    /// filter, finish any sort the adapter didn't handle, recount when the residual changed the set, then
    /// paginate after — unless the adapter already paginated natively (only valid when there was no residual).
    /// </summary>
    public static FinalizedQuery<TEntity> Finalize<TEntity>(
        QueryDefinition query,
        Filter? residual,
        RepositoryQueryResult<TEntity> adapter)
    {
        IReadOnlyList<TEntity> items = adapter.Items;
        var residualApplied = false;

        if (residual is not null)
        {
            var predicate = InMemoryFilterEvaluator.Compile<TEntity>(residual);
            items = items.Where(predicate).ToList();
            residualApplied = true;
        }

        var sortFallback = query.HasSort && !adapter.SortFullyHandled(query);
        if (sortFallback)
            items = InMemorySorter.Apply(items, query.Sort);

        long total;
        var isEstimate = adapter.IsEstimate;
        if (!residualApplied && adapter.TotalCount is { } adapterTotal)
        {
            total = adapterTotal;
        }
        else
        {
            total = items.Count;
            isEstimate = false;
        }

        var page = items;
        if (query.HasPagination && !adapter.PaginationHandled)
        {
            var pageSize = query.EffectivePageSize();
            var skip = query.EffectiveOffset();
            page = items.Skip(skip).Take(pageSize).ToList();
        }

        var fellBack = residualApplied || sortFallback || (query.HasPagination && !adapter.PaginationHandled);
        return new FinalizedQuery<TEntity>(page, total, isEstimate, fellBack);
    }
}
