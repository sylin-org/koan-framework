using System.Linq.Expressions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;

namespace Koan.Data.Core.Sorting;

/// <summary>
/// Fluent helpers for attaching structured sort specs to a <see cref="QueryDefinition"/>
/// (DATA-0092 + DATA-XXXX). Mirrors the <see cref="ISortBuilder{T}"/> API but targets the
/// query definition directly for the Data&lt;T&gt; facade.
/// </summary>
public static class QueryDefinitionSortExtensions
{
    public static QueryDefinition WithSort<T>(this QueryDefinition def, string? expression)
        => def.WithSort(SortSpecParser.ParseStrict<T>(expression));

    public static QueryDefinition WithSort<T>(this QueryDefinition def, Action<ISortBuilder<T>> configure)
        => def.WithSort(SortBuilder<T>.Build(configure));

    public static QueryDefinition ThenBy<T>(this QueryDefinition def, string token)
    {
        var spec = SortSpecParser.ParseSingleStrict(typeof(T), token);
        return def.WithSort(def.Sort.Append(spec).ToList());
    }

    public static QueryDefinition ThenBy<T, TKey>(this QueryDefinition def, Expression<Func<T, TKey>> selector)
    {
        var spec = SortSpecParser.Build(ExpressionMemberPath.From(selector), false);
        return def.WithSort(def.Sort.Append(spec).ToList());
    }

    public static QueryDefinition ThenByDescending<T, TKey>(this QueryDefinition def, Expression<Func<T, TKey>> selector)
    {
        var spec = SortSpecParser.Build(ExpressionMemberPath.From(selector), true);
        return def.WithSort(def.Sort.Append(spec).ToList());
    }
}
