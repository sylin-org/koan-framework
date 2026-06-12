using System.Linq.Expressions;
using System.Reflection;
using Koan.Data.Abstractions.Sorting;

namespace Koan.Data.Core.Sorting;

/// <summary>
/// LINQ-familiar builder for constructing <see cref="SortSpec"/> sequences with compile-time field validation.
/// </summary>
public interface ISortBuilder<T>
{
    /// <summary>Adds an ascending sort by the selected member.</summary>
    ISortBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> selector);

    /// <summary>Adds a descending sort by the selected member.</summary>
    ISortBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> selector);

    /// <summary>Adds an ascending tiebreaker sort.</summary>
    ISortBuilder<T> ThenBy<TKey>(Expression<Func<T, TKey>> selector);

    /// <summary>Adds a descending tiebreaker sort.</summary>
    ISortBuilder<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> selector);
}

public sealed class SortBuilder<T> : ISortBuilder<T>
{
    private readonly List<SortSpec> _specs = new();

    public IReadOnlyList<SortSpec> Build() => _specs;

    public ISortBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> selector)
        => Add(selector, desc: false);

    public ISortBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> selector)
        => Add(selector, desc: true);

    public ISortBuilder<T> ThenBy<TKey>(Expression<Func<T, TKey>> selector)
        => Add(selector, desc: false);

    public ISortBuilder<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> selector)
        => Add(selector, desc: true);

    private SortBuilder<T> Add<TKey>(Expression<Func<T, TKey>> selector, bool desc)
    {
        var path = ExpressionMemberPath.From(selector);
        _specs.Add(SortSpecParser.Build(path, desc));
        return this;
    }

    /// <summary>Convenience: build via configurator action. Returns empty list when configurator is null.</summary>
    public static IReadOnlyList<SortSpec> Build(Action<ISortBuilder<T>>? configure)
    {
        if (configure is null) return Array.Empty<SortSpec>();
        var b = new SortBuilder<T>();
        configure(b);
        return b.Build();
    }
}

public static class ExpressionMemberPath
{
    public static MemberPath From<T, TKey>(Expression<Func<T, TKey>> selector)
    {
        if (selector is null) throw new ArgumentNullException(nameof(selector));

        var body = Unwrap(selector.Body);
        var stack = new Stack<MemberInfo>();
        var traversesCollection = false;
        var collectionIdx = -1;

        while (body is MemberExpression me)
        {
            stack.Push(me.Member);
            body = Unwrap(me.Expression!);
        }

        if (body is not ParameterExpression || stack.Count == 0)
            throw new ArgumentException(
                $"Sort selector must be a chain of member accesses against the entity parameter (e.g. x => x.Foo.Bar). Got: {selector.Body}",
                nameof(selector));

        var members = stack.ToArray();
        var rootType = typeof(T);
        var currentType = rootType;
        var leafValueType = currentType;

        for (var i = 0; i < members.Length; i++)
        {
            var prop = members[i] as PropertyInfo;
            var fld = members[i] as FieldInfo;
            var memberType = prop?.PropertyType ?? fld?.FieldType
                ?? throw new ArgumentException($"Unsupported member kind: {members[i].MemberType}", nameof(selector));

            // If currentType is a collection and we still have segments to walk, switch to element type.
            var element = MemberPathResolver.TryGetCollectionElementType(currentType);
            if (element is not null && i > 0)
            {
                traversesCollection = true;
                if (collectionIdx < 0) collectionIdx = i;
                currentType = element;
            }
            currentType = memberType;
            leafValueType = memberType;
        }

        var leafElement = MemberPathResolver.TryGetCollectionElementType(leafValueType);
        if (leafElement is not null)
        {
            traversesCollection = true;
            if (collectionIdx < 0) collectionIdx = members.Length - 1;
            leafValueType = leafElement;
        }

        return new MemberPath(rootType, members, leafValueType, traversesCollection, collectionIdx);
    }

    private static Expression Unwrap(Expression expr)
    {
        while (expr is UnaryExpression u && (u.NodeType == ExpressionType.Convert || u.NodeType == ExpressionType.ConvertChecked))
        {
            expr = u.Operand;
        }
        return expr;
    }
}
