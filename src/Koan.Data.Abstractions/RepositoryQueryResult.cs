using System.Collections.Frozen;
using Koan.Data.Abstractions.Sorting;

namespace Koan.Data.Abstractions;

/// <summary>
/// A repository query result enriched with per-axis metadata describing what the adapter
/// actually pushed down. The coordinator inspects these flags to decide which residual work
/// to do in memory: evaluate the unpushed filter, sort the unhandled specs, paginate, or
/// project — always after the residual filter so paging is correct.
/// </summary>
public sealed class RepositoryQueryResult<TEntity>
{
    public static readonly IReadOnlySet<SortSpec> NoSortHandled = FrozenSet<SortSpec>.Empty;

    /// <summary>Items returned by the adapter, in the order it intends (sorted iff it pushed sort down).</summary>
    public required IReadOnlyList<TEntity> Items { get; init; }

    /// <summary>Total cardinality of the unpaginated result; null when the adapter could not compute it cheaply.</summary>
    public long? TotalCount { get; init; }

    /// <summary>True when <see cref="TotalCount"/> is approximate (e.g. from table stats).</summary>
    public bool IsEstimate { get; init; }

    /// <summary>Sort specs the adapter pushed down. Empty means none — coordinator sorts in memory.</summary>
    public IReadOnlySet<SortSpec> SortHandled { get; init; } = NoSortHandled;

    /// <summary>True when the adapter applied pagination server-side.</summary>
    public bool PaginationHandled { get; init; }

    /// <summary>True when the adapter applied the projection server-side (column selection).</summary>
    public bool ProjectionHandled { get; init; }

    /// <summary>True when <see cref="SortHandled"/> covers every spec in the query.</summary>
    public bool SortFullyHandled(QueryDefinition query)
    {
        if (query.Sort.Count == 0) return true;
        if (SortHandled.Count == 0) return false;
        foreach (var spec in query.Sort)
            if (!SortHandled.Contains(spec)) return false;
        return true;
    }
}
