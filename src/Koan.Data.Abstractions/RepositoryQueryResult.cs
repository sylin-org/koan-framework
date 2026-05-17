using System.Collections.Frozen;
using Koan.Data.Abstractions.Sorting;

namespace Koan.Data.Abstractions;

/// <summary>
/// Adapter-side query result enriched with metadata about what the adapter actually pushed down.
/// The orchestrator inspects <see cref="SortHandled"/> and <see cref="PaginationHandled"/> to decide
/// whether in-memory fallback is needed.
/// </summary>
public sealed class RepositoryQueryResult<TEntity>
{
    public static readonly IReadOnlySet<SortSpec> NoSortHandled = FrozenSet<SortSpec>.Empty;

    /// <summary>Items returned by the adapter, in the order the adapter intends (sorted iff adapter pushed sort down).</summary>
    public required IReadOnlyList<TEntity> Items { get; init; }

    /// <summary>Total cardinality of the unpaginated result. Null when the adapter could not compute it cheaply.</summary>
    public long? TotalCount { get; init; }

    /// <summary>True when <see cref="TotalCount"/> is approximate (e.g. from table stats).</summary>
    public bool IsEstimate { get; init; }

    /// <summary>True when the adapter applied <see cref="DataQueryOptions.Page"/>/<see cref="DataQueryOptions.PageSize"/> server-side.</summary>
    public bool PaginationHandled { get; init; }

    /// <summary>Specs the adapter pushed down. Empty (default) means none — orchestrator must sort in memory.</summary>
    public IReadOnlySet<SortSpec> SortHandled { get; init; } = NoSortHandled;

    /// <summary>True when SortHandled covers every spec in the requested options.</summary>
    public bool SortFullyHandled(DataQueryOptions? options)
    {
        if (options is null || options.Sort.Count == 0) return true;
        if (SortHandled.Count == 0) return false;
        foreach (var spec in options.Sort)
        {
            if (!SortHandled.Contains(spec)) return false;
        }
        return true;
    }

    /// <summary>Convenience: build a result for an adapter that handled neither sort nor pagination.</summary>
    public static RepositoryQueryResult<TEntity> Unhandled(IReadOnlyList<TEntity> items, long? totalCount = null, bool isEstimate = false)
        => new()
        {
            Items = items,
            TotalCount = totalCount,
            IsEstimate = isEstimate,
            PaginationHandled = false,
            SortHandled = NoSortHandled,
        };

    /// <summary>Convenience: build a result where pagination was server-side but sort was not (or none requested).</summary>
    public static RepositoryQueryResult<TEntity> PaginatedOnly(IReadOnlyList<TEntity> items, long? totalCount = null, bool isEstimate = false)
        => new()
        {
            Items = items,
            TotalCount = totalCount,
            IsEstimate = isEstimate,
            PaginationHandled = true,
            SortHandled = NoSortHandled,
        };

    /// <summary>Convenience: build a result where the adapter handled both sort (all requested specs) and pagination.</summary>
    public static RepositoryQueryResult<TEntity> FullyHandled(IReadOnlyList<TEntity> items, IReadOnlySet<SortSpec> sortHandled, long? totalCount = null, bool isEstimate = false)
        => new()
        {
            Items = items,
            TotalCount = totalCount,
            IsEstimate = isEstimate,
            PaginationHandled = true,
            SortHandled = sortHandled,
        };
}
