using System.Collections;
using System.Reflection;
using Koan.Data.Abstractions.Sorting;

namespace Koan.Data.Core.Sorting;

/// <summary>
/// Applies a sequence of <see cref="SortSpec"/>s in-memory against an enumerable. Honours collection
/// aggregation hints. Returns a new list; never mutates the input.
/// </summary>
public static class InMemorySorter
{
    /// <summary>Applies the specs to the source, returning a sorted list. Empty/null specs returns the source materialised.</summary>
    public static IReadOnlyList<T> Apply<T>(IEnumerable<T> source, IReadOnlyList<SortSpec> specs)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (specs is null || specs.Count == 0)
            return source as IReadOnlyList<T> ?? source.ToList();

        IOrderedEnumerable<T>? ordered = null;
        foreach (var spec in specs)
        {
            var selector = MakeSelector<T>(spec);
            ordered = ordered is null
                ? (spec.Desc ? source.OrderByDescending(selector, ValueComparer.Instance) : source.OrderBy(selector, ValueComparer.Instance))
                : (spec.Desc ? ordered.ThenByDescending(selector, ValueComparer.Instance) : ordered.ThenBy(selector, ValueComparer.Instance));
        }
        return ordered!.ToList();
    }

    private static Func<T, object?> MakeSelector<T>(SortSpec spec)
    {
        var path = spec.Path;
        var members = path.Members;
        var aggregation = spec.Aggregation;

        return entity =>
        {
            object? current = entity;
            for (var i = 0; i < members.Count; i++)
            {
                if (current is null) return null;

                // If the current value is a collection (and we have more members to walk OR aggregation is requested),
                // aggregate over its elements first.
                if (current is IEnumerable enumerable && current is not string && IsCollectionShape(current.GetType()))
                {
                    return AggregateOverCollection(enumerable, members, i, aggregation);
                }

                current = GetMemberValue(members[i], current);
            }

            // Leaf might still be a collection (rare).
            if (current is IEnumerable leafEnum && current is not string && IsCollectionShape(current.GetType()))
            {
                return AggregateScalar(leafEnum, aggregation);
            }

            return current;
        };
    }

    private static object? AggregateOverCollection(IEnumerable source, IReadOnlyList<MemberInfo> members, int startIdx, SortAggregation aggregation)
    {
        var values = new List<object?>();
        foreach (var element in source)
        {
            if (element is null) { values.Add(null); continue; }
            object? cursor = element;
            for (var j = startIdx; j < members.Count; j++)
            {
                if (cursor is null) break;
                cursor = GetMemberValue(members[j], cursor);
            }
            if (cursor is IEnumerable nested && cursor is not string && IsCollectionShape(cursor.GetType()))
            {
                values.Add(AggregateScalar(nested, aggregation));
            }
            else
            {
                values.Add(cursor);
            }
        }
        return AggregateScalar(values, aggregation);
    }

    private static object? AggregateScalar(IEnumerable values, SortAggregation aggregation)
    {
        object? acc = null;
        var seen = false;
        var enumerator = values.GetEnumerator();
        switch (aggregation)
        {
            case SortAggregation.First:
                if (enumerator.MoveNext()) return enumerator.Current;
                return null;
            case SortAggregation.Last:
                while (enumerator.MoveNext()) { acc = enumerator.Current; seen = true; }
                return seen ? acc : null;
            case SortAggregation.Max:
            case SortAggregation.None: // None on a collection leaf falls back to Max — direction-agnostic safe default.
                while (enumerator.MoveNext())
                {
                    var v = enumerator.Current;
                    if (!seen || ValueComparer.Instance.Compare(v, acc) > 0) { acc = v; seen = true; }
                }
                return seen ? acc : null;
            case SortAggregation.Min:
                while (enumerator.MoveNext())
                {
                    var v = enumerator.Current;
                    if (!seen || ValueComparer.Instance.Compare(v, acc) < 0) { acc = v; seen = true; }
                }
                return seen ? acc : null;
            default:
                return null;
        }
    }

    private static object? GetMemberValue(MemberInfo member, object instance)
        => member switch
        {
            PropertyInfo p => p.GetValue(instance),
            FieldInfo f => f.GetValue(instance),
            _ => null
        };

    private static bool IsCollectionShape(Type t)
    {
        if (t == typeof(string)) return false;
        if (t.IsArray) return true;
        foreach (var iface in t.GetInterfaces())
        {
            if (iface == typeof(IEnumerable)) return true;
        }
        return false;
    }

    private sealed class ValueComparer : IComparer<object?>
    {
        public static readonly ValueComparer Instance = new();

        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            if (x is IComparable cx && x.GetType() == y.GetType())
                return cx.CompareTo(y);

            return string.Compare(x.ToString(), y.ToString(), StringComparison.Ordinal);
        }
    }
}
