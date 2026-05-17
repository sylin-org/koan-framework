using Koan.Data.Abstractions.Sorting;

namespace Koan.Data.Abstractions;

/// <summary>
/// Paging and query shaping hints that can flow through repositories without depending on web-layer types.
/// </summary>
public sealed class DataQueryOptions
{
    public DataQueryOptions()
    {
    }

    public DataQueryOptions(int? page = null, int? pageSize = null)
    {
        Page = page;
        PageSize = pageSize;
    }

    /// <summary>
    /// 1-based page number when pagination is active.
    /// </summary>
    public int? Page { get; init; }

    /// <summary>
    /// Page size when pagination is active.
    /// </summary>
    public int? PageSize { get; init; }

    /// <summary>
    /// Optional repository-specific filter representation.
    /// </summary>
    public string? Filter { get; init; }

    /// <summary>
    /// Structured sort specifications resolved against the target entity type. Empty when no sort is requested.
    /// Adapters translate to native syntax where possible; the orchestrator handles the rest in memory.
    /// </summary>
    public IReadOnlyList<SortSpec> Sort { get; init; } = Array.Empty<SortSpec>();

    /// <summary>
    /// Logical partition name for repositories that support sharding.
    /// </summary>
    public string? Partition { get; init; }

    /// <summary>
    /// Optional hint for which count strategy should be used.
    /// </summary>
    public CountStrategy? CountStrategy { get; init; }

    public bool HasPagination => Page.HasValue && Page.Value > 0 && PageSize.HasValue && PageSize.Value > 0;

    public bool HasSort => Sort is { Count: > 0 };

    public int EffectivePage(int defaultValue = 1)
        => Page.HasValue && Page.Value > 0 ? Page.Value : defaultValue;

    public int EffectivePageSize(int defaultValue = 50)
        => PageSize.HasValue && PageSize.Value > 0 ? PageSize.Value : defaultValue;

    public DataQueryOptions WithPagination(int page, int pageSize)
    {
        if (page <= 0) throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));

        return new DataQueryOptions
        {
            Page = page,
            PageSize = pageSize,
            Filter = Filter,
            Sort = Sort,
            Partition = Partition,
            CountStrategy = CountStrategy
        };
    }

    public DataQueryOptions WithoutPagination()
        => new()
        {
            Filter = Filter,
            Sort = Sort,
            Partition = Partition,
            CountStrategy = CountStrategy
        };

    public DataQueryOptions WithFilter(string? filter)
        => new()
        {
            Page = Page,
            PageSize = PageSize,
            Filter = filter,
            Sort = Sort,
            Partition = Partition,
            CountStrategy = CountStrategy
        };

    /// <summary>Replaces the current sort with the provided structured specs.</summary>
    public DataQueryOptions WithSort(IReadOnlyList<SortSpec> sort)
        => new()
        {
            Page = Page,
            PageSize = PageSize,
            Filter = Filter,
            Sort = sort ?? Array.Empty<SortSpec>(),
            Partition = Partition,
            CountStrategy = CountStrategy
        };

    /// <summary>Removes all sort specs.</summary>
    public DataQueryOptions WithoutSort()
        => new()
        {
            Page = Page,
            PageSize = PageSize,
            Filter = Filter,
            Sort = Array.Empty<SortSpec>(),
            Partition = Partition,
            CountStrategy = CountStrategy
        };

    public DataQueryOptions ForPartition(string? partition)
        => new()
        {
            Page = Page,
            PageSize = PageSize,
            Filter = Filter,
            Sort = Sort,
            Partition = partition,
            CountStrategy = CountStrategy
        };

    public DataQueryOptions WithCountStrategy(CountStrategy? strategy)
        => new()
        {
            Page = Page,
            PageSize = PageSize,
            Filter = Filter,
            Sort = Sort,
            Partition = Partition,
            CountStrategy = strategy
        };
}
