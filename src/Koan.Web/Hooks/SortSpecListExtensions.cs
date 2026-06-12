using System.Linq.Expressions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core.Sorting;

namespace Koan.Web.Hooks;

/// <summary>
/// Convenience extensions on <see cref="System.Collections.Generic.IList{T}"/> of <see cref="SortSpec"/>
/// for hook implementations. Lets hooks add sort specs by field name or LINQ selector without
/// constructing <see cref="MemberPath"/> by hand. Resolution happens at call time against
/// the supplied entity type — throws <see cref="InvalidSortFieldException"/> for unresolvable fields.
/// </summary>
public static class SortSpecListExtensions
{
    /// <summary>Appends a sort spec parsed from a token like "-CreatedAt" or "+Name".</summary>
    public static void AddByField<TEntity>(this IList<SortSpec> sorts, string token)
        => sorts.Add(SortSpecParser.ParseSingleStrict(typeof(TEntity), token));

    /// <summary>Appends a sort spec for the named field, with explicit direction.</summary>
    public static void AddByField<TEntity>(this IList<SortSpec> sorts, string field, bool desc)
    {
        var path = MemberPathResolver.ResolveStrict(typeof(TEntity), field);
        sorts.Add(SortSpecParser.Build(path, desc));
    }

    /// <summary>Appends an ascending sort spec from a LINQ key selector.</summary>
    public static void OrderBy<TEntity, TKey>(this IList<SortSpec> sorts, Expression<Func<TEntity, TKey>> selector)
    {
        var path = ExpressionMemberPath.From(selector);
        sorts.Add(SortSpecParser.Build(path, desc: false));
    }

    /// <summary>Appends a descending sort spec from a LINQ key selector.</summary>
    public static void OrderByDescending<TEntity, TKey>(this IList<SortSpec> sorts, Expression<Func<TEntity, TKey>> selector)
    {
        var path = ExpressionMemberPath.From(selector);
        sorts.Add(SortSpecParser.Build(path, desc: true));
    }
}
