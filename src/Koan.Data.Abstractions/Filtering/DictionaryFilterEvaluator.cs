using System.Collections;
using System.Globalization;

namespace Koan.Data.Abstractions.Filtering;

/// <summary>
/// Executes a <see cref="Filter"/> against schemaless metadata bags
/// (<see cref="IReadOnlyDictionary{TKey,TValue}"/> keyed by metadata key). This is the
/// <b>convergence oracle</b> for the vector path (AI-0036 §10 / DATA-0097 P1): the reference
/// result-set every vector adapter's native pushdown must match over a seeded corpus, and the
/// filter the in-memory test adapter applies.
/// </summary>
/// <remarks>
/// It is the dictionary-backed twin of <see cref="InMemoryFilterEvaluator"/> and implements the
/// <b>identical locked null/Nin/HasNone semantics</b>: null/absent is not a member of any set, so
/// <see cref="FilterOperator.Nin"/>/<see cref="FilterOperator.HasNone"/> match null/missing while
/// <see cref="FilterOperator.In"/>/<see cref="FilterOperator.HasAny"/> do not; relational
/// comparisons are false when either side is null. The differences from the CLR evaluator are
/// forced by schemalessness: fields are resolved as metadata keys (not via
/// <c>FieldPathResolver</c>), collection-ness is decided by the <i>runtime</i> value (an
/// <see cref="IEnumerable"/> that is not a string), and there is no static <c>ComparableType</c>, so
/// comparison is numeric-tolerant (any two numeric values compare by their <see cref="decimal"/>/
/// <see cref="double"/> magnitude) rather than CLR-type-bound.
/// </remarks>
public static class DictionaryFilterEvaluator
{
    /// <summary>Compiles a filter into a reusable predicate over a metadata bag.</summary>
    public static Func<IReadOnlyDictionary<string, object?>, bool> Compile(Filter filter)
    {
        var predicate = Build(filter);
        return bag => bag is not null && predicate(bag);
    }

    /// <summary>Convenience: filter a sequence of metadata bags.</summary>
    public static IEnumerable<IReadOnlyDictionary<string, object?>> Apply(
        IEnumerable<IReadOnlyDictionary<string, object?>> source, Filter filter)
        => source.Where(Compile(filter));

    private static Func<IReadOnlyDictionary<string, object?>, bool> Build(Filter filter)
    {
        switch (filter)
        {
            case AllOf all:
            {
                var ps = all.Operands.Select(Build).ToArray();
                return b => { foreach (var p in ps) if (!p(b)) return false; return true; };
            }
            case AnyOf any:
            {
                var ps = any.Operands.Select(Build).ToArray();
                return b => { foreach (var p in ps) if (p(b)) return true; return false; };
            }
            case Not n:
            {
                var inner = Build(n.Operand);
                return b => !inner(b);
            }
            case ClrFilter:
                // No CLR projection over a metadata bag — opaque predicates are forbidden on the vector path.
                throw new NotSupportedException("ClrFilter cannot be evaluated against schemaless vector metadata.");
            case FieldFilter f:
                return BuildField(f);
            default:
                throw new NotSupportedException($"Unknown filter node '{filter.GetType().Name}'.");
        }
    }

    private static Func<IReadOnlyDictionary<string, object?>, bool> BuildField(FieldFilter f)
    {
        var op = f.Operator;
        var ic = f.IgnoreCase;
        var path = f.Field.Segments;

        if (op == FilterOperator.Exists)
        {
            var desired = Scalar(f.Value) as bool? ?? true;
            return b => (Resolve(b, path) is not null) == desired;
        }

        if (op == FilterOperator.Size)
        {
            var count = ToInt(Scalar(f.Value));
            return b => CountOf(Resolve(b, path)) == count;
        }

        if (op is FilterOperator.Has or FilterOperator.HasAny or FilterOperator.HasAll or FilterOperator.HasNone)
        {
            var set = SetRaw(f.Value);
            var single = Scalar(f.Value);
            return b => EvaluateCollection(op, Resolve(b, path), set, single, ic);
        }

        if (op is FilterOperator.In or FilterOperator.Nin)
        {
            var set = SetRaw(f.Value);
            return op == FilterOperator.In
                ? b => InSet(Resolve(b, path), set, ic)
                : b => !InSet(Resolve(b, path), set, ic); // Nin matches null/missing (locked)
        }

        var rhs = Scalar(f.Value);
        var cmp = ic ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return op switch
        {
            FilterOperator.Eq => b => ValEq(Resolve(b, path), rhs, ic),
            FilterOperator.Ne => b => !ValEq(Resolve(b, path), rhs, ic),
            FilterOperator.Gt or FilterOperator.Gte or FilterOperator.Lt or FilterOperator.Lte
                => b => CompareSatisfies(op, Resolve(b, path), rhs),
            FilterOperator.StartsWith => b => Resolve(b, path) is string s && rhs is string p && s.StartsWith(p, cmp),
            FilterOperator.EndsWith => b => Resolve(b, path) is string s && rhs is string p && s.EndsWith(p, cmp),
            FilterOperator.Contains => b => Resolve(b, path) is string s && rhs is string p && s.Contains(p, cmp),
            _ => throw new NotSupportedException($"Operator '{op}' is not valid on metadata field '{f.Field}'.")
        };
    }

    private static bool EvaluateCollection(FilterOperator op, object? raw, IReadOnlyList<object?> set, object? single, bool ic)
    {
        var items = Materialize(raw);
        return op switch
        {
            FilterOperator.Has => items.Any(i => ValEq(i, single, ic)),
            FilterOperator.HasAny => items.Any(i => InSet(i, set, ic)),
            FilterOperator.HasAll => set.All(x => items.Any(i => ValEq(i, x, ic))),
            FilterOperator.HasNone => !items.Any(i => InSet(i, set, ic)), // null/empty disjoint => matches (locked)
            _ => throw new NotSupportedException($"Operator '{op}' is not valid on a collection metadata field.")
        };
    }

    // --- metadata-bag field resolution (nested dot-path through nested dictionaries) ---

    private static object? Resolve(IReadOnlyDictionary<string, object?> bag, IReadOnlyList<string> segments)
    {
        object? current = bag;
        foreach (var seg in segments)
        {
            if (current is IReadOnlyDictionary<string, object?> rod)
            {
                if (!rod.TryGetValue(seg, out current)) return null;
            }
            else if (current is IDictionary<string, object?> d)
            {
                if (!d.TryGetValue(seg, out current)) return null;
            }
            else if (current is IDictionary raw)
            {
                if (!raw.Contains(seg)) return null;
                current = raw[seg];
            }
            else
            {
                return null;
            }
        }
        return current;
    }

    // --- value helpers (numeric-tolerant, no CLR target type) ---

    private static object? Scalar(FilterValue v) => v switch
    {
        FilterValue.Scalar s => s.Value,
        FilterValue.Set st => st.Values.Count > 0 ? st.Values[0] : null,
        _ => null
    };

    private static IReadOnlyList<object?> SetRaw(FilterValue v) => v switch
    {
        FilterValue.Set st => st.Values,
        FilterValue.Scalar s => new[] { s.Value },
        _ => Array.Empty<object?>()
    };

    private static bool ValEq(object? a, object? b, bool ic)
    {
        if (a is null) return b is null;
        if (b is null) return false;
        if (a is string sa && b is string sb)
            return string.Equals(sa, sb, ic ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        if (TryNum(a, out var na) && TryNum(b, out var nb)) return na == nb;
        if (a is bool || b is bool) return a.Equals(b);
        return a.Equals(b);
    }

    private static bool InSet(object? value, IReadOnlyList<object?> set, bool ic)
    {
        foreach (var x in set) if (ValEq(value, x, ic)) return true;
        return false;
    }

    private static bool CompareSatisfies(FilterOperator op, object? a, object? b)
    {
        if (a is null || b is null) return false; // comparisons with null are false (locked)
        int c;
        if (TryNum(a, out var na) && TryNum(b, out var nb)) c = na.CompareTo(nb);
        else if (a is string sa && b is string sb) c = string.CompareOrdinal(sa, sb);
        else if (a is IComparable ca) c = ca.CompareTo(b);
        else return false;
        return op switch
        {
            FilterOperator.Gt => c > 0,
            FilterOperator.Gte => c >= 0,
            FilterOperator.Lt => c < 0,
            FilterOperator.Lte => c <= 0,
            _ => false
        };
    }

    private static bool TryNum(object? v, out decimal d)
    {
        switch (v)
        {
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                d = Convert.ToDecimal(v, CultureInfo.InvariantCulture); return true;
            case float fl: d = (decimal)fl; return true;
            case double db: d = (decimal)db; return true;
            case decimal de: d = de; return true;
            default: d = default; return false;
        }
    }

    private static int ToInt(object? v) => v is null ? 0 : (TryNum(v, out var d) ? (int)d : 0);

    private static List<object?> Materialize(object? raw)
    {
        var list = new List<object?>();
        if (raw is string) return list;            // a string is a scalar, not a collection
        if (raw is IEnumerable col)
            foreach (var x in col) list.Add(x);
        return list;
    }

    private static int CountOf(object? raw)
    {
        if (raw is null or string) return 0;
        if (raw is ICollection c) return c.Count;
        if (raw is IEnumerable e) { var n = 0; foreach (var _ in e) n++; return n; }
        return 0;
    }
}
