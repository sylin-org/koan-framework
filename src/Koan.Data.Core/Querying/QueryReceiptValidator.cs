using Koan.Data.Abstractions;

namespace Koan.Data.Core.Querying;

/// <summary>Validates that a provider result can safely satisfy or fall back from each requested query axis.</summary>
internal static class QueryReceiptValidator
{
    public static void Validate<TEntity>(QueryDefinition adapterQuery, RepositoryQueryResult<TEntity> result)
    {
        var entityType = typeof(TEntity).FullName ?? typeof(TEntity).Name;

        if (adapterQuery.Filter is not null && !result.FilterHandled)
            throw Reject(QueryReceiptAxis.Filter,
                "The adapter must apply the complete filter it received and report FilterHandled=true.");

        if (!adapterQuery.HasPagination && result.PaginationHandled)
            throw Reject(QueryReceiptAxis.Pagination,
                "The adapter reported provider pagination for a definition that contained no page.");

        if (adapterQuery.HasPagination && result.PaginationHandled &&
            result.Items.Count > adapterQuery.EffectivePageSize())
            throw Reject(QueryReceiptAxis.Bound,
                "The adapter returned more records than the provider page it reported handling.");

        foreach (var handled in result.SortHandled)
            if (!adapterQuery.Sort.Contains(handled))
                throw Reject(QueryReceiptAxis.Sort,
                    "The adapter reported a sort component that was not present in its definition.");

        if (adapterQuery.CountStrategy is null)
        {
            if (result.TotalCount is not null || result.CountExecution != CountExecutionKind.None)
                throw Reject(QueryReceiptAxis.Count,
                    "The adapter performed or reported count work when the definition requested none.");
        }
        else
        {
            if (result.TotalCount is null && result.CountExecution != CountExecutionKind.None)
                throw Reject(QueryReceiptAxis.Count,
                    "The adapter reported count execution without a total.");
            if (result.TotalCount is not null && result.CountExecution == CountExecutionKind.None)
                throw Reject(QueryReceiptAxis.Count,
                    "The adapter returned a total without reporting the count work that produced it.");
            if (result.CountExecution is CountExecutionKind.Exact or CountExecutionKind.Optimized && result.IsEstimate)
                throw Reject(QueryReceiptAxis.Count,
                    "Exact or optimized-exact count execution cannot be marked as an estimate.");
            if (adapterQuery.HasPagination && result.PaginationHandled && result.TotalCount is null)
                throw Reject(QueryReceiptAxis.Count,
                    "A provider-paged query that requests a total must return the unpaginated total.");
        }

        return;

        QueryReceiptRejectedException Reject(QueryReceiptAxis axis, string correction)
            => new(entityType, axis, correction);
    }
}
