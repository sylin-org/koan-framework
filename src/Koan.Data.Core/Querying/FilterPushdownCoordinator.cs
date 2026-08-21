using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Sorting;
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
        query = EnsureOrderForPage(query, entityType);
        if (query.Filter is null) return (query, null);

        var split = FilterSplitter.Split(query.Filter, caps, entityType);
        var adapterQuery = query.Where(split.Pushable);
        if (split.Residual is not null)
            adapterQuery = adapterQuery
                .WithoutPagination()
                .WithCountStrategy(null);
        return (adapterQuery, split.Residual);
    }

    /// <summary>
    /// Supplies the order a page is taken against when the caller did not name one.
    ///
    /// <para>A page is a window onto an order, so paging without one is not a weaker query — it is a
    /// meaningless one, and the store is free to return different rows for page two than the rows page one
    /// implied. Each adapter used to answer this privately, and one of the answers was
    /// <c>ORDER BY (SELECT NULL)</c>: enough to satisfy SQL Server's requirement that OFFSET have an ORDER BY,
    /// and no ordering at all. Two successive pages could then repeat and skip rows, on the most ordinary
    /// operation there is.</para>
    ///
    /// <para>So the decision moves here, where it is made once and every adapter inherits it. This is the same
    /// rule <see cref="QueryStreamCoordinator"/> already applies to streams, for the same reason; a stream that
    /// reached this point has applied it already and is left alone.</para>
    ///
    /// <para>Only paginated queries pay for it. An unpaged read has no window to be a window of, so it keeps
    /// whatever order the store finds cheapest.</para>
    /// </summary>
    private static QueryDefinition EnsureOrderForPage(QueryDefinition query, Type entityType)
    {
        if (!query.HasPagination || query.HasSort) return query;
        if (AggregateMetadata.GetIdSpec(entityType)?.Prop is not { } identity) return query;
        return query.WithSort([new SortSpec(
            new MemberPath(entityType, [identity], identity.PropertyType, traversesCollection: false, collectionSegmentIndex: -1),
            Desc: false)]);
    }

    /// <summary>
    /// Finalize an adapter result against the original query and the planned residual: apply the residual
    /// filter, finish any sort the adapter didn't handle, recount when the residual changed the set, then
    /// paginate after — unless the adapter already paginated natively (only valid when there was no residual).
    /// </summary>
    public static FinalizedQuery<TEntity> Finalize<TEntity>(
        QueryDefinition query,
        QueryDefinition adapterQuery,
        Filter? residual,
        RepositoryQueryResult<TEntity> adapter)
    {
        QueryReceiptValidator.Validate(adapterQuery, adapter);
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

        var paginationFallback = query.HasPagination && !adapter.PaginationHandled;
        var fellBack = residualApplied || sortFallback || paginationFallback;
        if (fellBack)
            QueryFallbackFacts.Record<TEntity>(residualApplied, sortFallback, paginationFallback);
        return new FinalizedQuery<TEntity>(page, total, isEstimate, fellBack);
    }
}
