using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Sorting;

namespace Koan.Data.Abstractions;

/// <summary>
/// The single, structured description of an entity query — the one value object that flows
/// from the entity-first facade / web layer to a repository, replacing both the untyped
/// <c>object? query</c> slot and the former <c>DataQueryOptions</c>. It bundles every query
/// axis: the <see cref="Filter"/> (normalized AST), <see cref="Sort"/>, <see cref="Projection"/>,
/// pagination, <see cref="Partition"/>, and count strategy.
///
/// Being a record, the seven hand-rolled <c>With*</c> copiers of the old options class collapse
/// into <c>with</c> expressions; the fluent helpers below are thin wrappers for the common shapes.
/// A null <see cref="Filter"/> means "match all".
/// </summary>
public sealed record QueryDefinition
{
    /// <summary>An unconstrained query (no filter, sort, projection, or pagination).</summary>
    public static QueryDefinition All { get; } = new();

    public Filter? Filter { get; init; }
    public IReadOnlyList<SortSpec> Sort { get; init; } = Array.Empty<SortSpec>();
    public Projection? Projection { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
    public string? Partition { get; init; }
    /// <summary>
    /// Requested total-count strategy. A null value means the query caller does not need a total;
    /// adapters must not issue count work merely because pagination is present.
    /// </summary>
    public CountStrategy? CountStrategy { get; init; }

    public bool HasFilter => Filter is not null;
    public bool HasSort => Sort.Count > 0;
    public bool HasProjection => Projection is not null;
    public bool HasPagination => Page is > 0 && PageSize is > 0;

    public int EffectivePage(int fallback = 1) => Page is > 0 ? Page.Value : fallback;
    public int EffectivePageSize(int fallback = 50) => PageSize is > 0 ? PageSize.Value : fallback;
    /// <summary>
    /// Returns the zero-based provider offset and rejects a range that cannot be represented by the
    /// current Int32 Skip/OFFSET contract.
    /// </summary>
    public int EffectiveOffset()
    {
        if (!HasPagination) return 0;
        var offset = ((long)EffectivePage() - 1L) * EffectivePageSize();
        if (offset > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(Page), Page,
                "The requested page exceeds the supported provider offset range.");
        return (int)offset;
    }

    public QueryDefinition Where(Filter? filter) => this with { Filter = filter };
    public QueryDefinition WithSort(IReadOnlyList<SortSpec> sort) => this with { Sort = sort ?? Array.Empty<SortSpec>() };
    public QueryDefinition WithProjection(Projection? projection) => this with { Projection = projection };
    public QueryDefinition WithPagination(int page, int pageSize)
    {
        if (page <= 0) throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
        var paged = this with { Page = page, PageSize = pageSize };
        _ = paged.EffectiveOffset();
        return paged;
    }
    public QueryDefinition WithoutPagination() => this with { Page = null, PageSize = null };
    public QueryDefinition ForPartition(string? partition) => this with { Partition = partition };
    public QueryDefinition WithCountStrategy(CountStrategy? strategy) => this with { CountStrategy = strategy };
}
