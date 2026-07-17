namespace Koan.Core.Ordering;

/// <summary>One deterministic Kahn ordering law shared by legacy and semantic composition.</summary>
internal static class StableTopologicalOrder
{
    public static bool TrySort<T>(
        IEnumerable<T> nodes,
        IEnumerable<(T From, T To)> edges,
        IComparer<T> comparer,
        out T[] ordered,
        out T[] residual,
        IEqualityComparer<T>? equalityComparer = null)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(comparer);

        equalityComparer ??= EqualityComparer<T>.Default;
        var nodeSet = new HashSet<T>(nodes, equalityComparer);
        var edgeSet = new HashSet<(T From, T To)>(new EdgeComparer<T>(equalityComparer));
        foreach (var edge in edges)
        {
            if (nodeSet.Contains(edge.From) && nodeSet.Contains(edge.To)) edgeSet.Add(edge);
        }

        var inDegree = nodeSet.ToDictionary(static node => node, static _ => 0, equalityComparer);
        foreach (var edge in edgeSet) inDegree[edge.To]++;
        var ready = new SortedSet<T>(comparer);
        foreach (var pair in inDegree)
        {
            if (pair.Value == 0) ready.Add(pair.Key);
        }

        var outgoing = edgeSet
            .GroupBy(static edge => edge.From, equalityComparer)
            .ToDictionary(
                static group => group.Key,
                group => group.Select(static edge => edge.To).OrderBy(value => value, comparer).ToArray(),
                equalityComparer);
        var result = new List<T>(nodeSet.Count);
        while (ready.Count > 0)
        {
            var next = ready.Min!;
            ready.Remove(next);
            result.Add(next);
            if (!outgoing.TryGetValue(next, out var dependents)) continue;
            foreach (var dependent in dependents)
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0) ready.Add(dependent);
            }
        }

        ordered = result.ToArray();
        residual = nodeSet
            .Where(node => !result.Contains(node, equalityComparer))
            .OrderBy(value => value, comparer)
            .ToArray();
        return residual.Length == 0;
    }

    private sealed class EdgeComparer<T>(IEqualityComparer<T> comparer) : IEqualityComparer<(T From, T To)>
    {
        public bool Equals((T From, T To) x, (T From, T To) y) =>
            comparer.Equals(x.From, y.From) && comparer.Equals(x.To, y.To);

        public int GetHashCode((T From, T To) obj) =>
            HashCode.Combine(comparer.GetHashCode(obj.From!), comparer.GetHashCode(obj.To!));
    }
}
