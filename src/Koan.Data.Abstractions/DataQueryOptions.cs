using System;

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
    /// Optional sort expression understood by the repository.
    /// </summary>
    public string? Sort { get; init; }

    /// <summary>
    /// Logical partition name for repositories that support sharding.
    /// </summary>
    public string? Partition { get; init; }

    /// <summary>
    /// Optional hint for which count strategy should be used.
    /// </summary>
    public CountStrategy? CountStrategy { get; init; }

    public bool HasPagination => Page.HasValue && Page.Value > 0 && PageSize.HasValue && PageSize.Value > 0;

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

    public DataQueryOptions WithSort(string? sort)
        => new()
        {
            Page = Page,
            PageSize = PageSize,
            Filter = Filter,
            Sort = sort,
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

// Optional: paging-aware base repository contract, enabling server-side pushdown

// Optional query capability: raw string query (e.g., SQL, JSON filter)

// Optional: paging-aware string-query contract

// Optional query capability: LINQ predicate

// Optional: paging-aware LINQ contract
