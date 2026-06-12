namespace Koan.Data.Abstractions.Sorting;

/// <summary>
/// How a sort spec aggregates when its <see cref="MemberPath"/> traverses a collection segment.
/// </summary>
public enum SortAggregation
{
    /// <summary>No aggregation; path resolves to a scalar.</summary>
    None,

    /// <summary>Pick the maximum value across collection elements. Default for descending sort over a collection.</summary>
    Max,

    /// <summary>Pick the minimum value across collection elements. Default for ascending sort over a collection.</summary>
    Min,

    /// <summary>Pick the first element's value (insertion order, adapter-defined).</summary>
    First,

    /// <summary>Pick the last element's value (insertion order, adapter-defined).</summary>
    Last
}
