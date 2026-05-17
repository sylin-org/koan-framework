using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;

namespace Koan.Data.Core.Sorting;

/// <summary>
/// Typed sort builders for <see cref="DataQueryOptions"/>. These extensions parse the URL grammar
/// or compile a LINQ-style configurator into structured <see cref="SortSpec"/>s against entity type T.
/// </summary>
public static class DataQueryOptionsSortExtensions
{
    /// <summary>Replaces sort with specs parsed from a URL-grammar string (strict). Throws <see cref="InvalidSortFieldException"/> on unresolvable fields.</summary>
    public static DataQueryOptions WithSort<T>(this DataQueryOptions opts, string? expression)
        => opts.WithSort(SortSpecParser.ParseStrict<T>(expression));

    /// <summary>Replaces sort with specs built via the LINQ-style configurator. Empty configurator clears the sort.</summary>
    public static DataQueryOptions WithSort<T>(this DataQueryOptions opts, Action<ISortBuilder<T>> configure)
        => opts.WithSort(SortBuilder<T>.Build(configure));

    /// <summary>Adds a single sort spec parsed from a token like "-CreatedAt" or "+Name" (strict).</summary>
    public static DataQueryOptions ThenBy<T>(this DataQueryOptions opts, string token)
    {
        var spec = SortSpecParser.ParseSingleStrict(typeof(T), token);
        var combined = new List<SortSpec>(opts.Sort.Count + 1);
        combined.AddRange(opts.Sort);
        combined.Add(spec);
        return opts.WithSort(combined);
    }

    /// <summary>Adds a single sort spec from a LINQ key selector (ascending).</summary>
    public static DataQueryOptions ThenBy<T, TKey>(this DataQueryOptions opts, System.Linq.Expressions.Expression<Func<T, TKey>> selector)
    {
        var path = ExpressionMemberPath.From(selector);
        var spec = SortSpecParser.Build(path, desc: false);
        var combined = new List<SortSpec>(opts.Sort.Count + 1);
        combined.AddRange(opts.Sort);
        combined.Add(spec);
        return opts.WithSort(combined);
    }

    /// <summary>Adds a single sort spec from a LINQ key selector (descending).</summary>
    public static DataQueryOptions ThenByDescending<T, TKey>(this DataQueryOptions opts, System.Linq.Expressions.Expression<Func<T, TKey>> selector)
    {
        var path = ExpressionMemberPath.From(selector);
        var spec = SortSpecParser.Build(path, desc: true);
        var combined = new List<SortSpec>(opts.Sort.Count + 1);
        combined.AddRange(opts.Sort);
        combined.Add(spec);
        return opts.WithSort(combined);
    }
}
