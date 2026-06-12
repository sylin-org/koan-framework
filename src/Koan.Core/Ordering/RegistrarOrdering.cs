namespace Koan.Core.Ordering;

/// <summary>
/// Deterministic topological sort of <see cref="IKoanInitializer"/> types
/// based on <see cref="BeforeAttribute"/> / <see cref="AfterAttribute"/>
/// declarations. See CORE-0091.
/// </summary>
/// <remarks>
/// <para>
/// Implements Kahn's algorithm with a stable tie-breaker on
/// <c>Type.AssemblyQualifiedName</c> so unconstrained nodes still sort the
/// same way across machines and runs. Replaces the previous
/// <c>ConcurrentDictionary.Keys.ToArray()</c> behavior which was hash-table-
/// bucket-dependent and non-deterministic.
/// </para>
/// <para>
/// Constraints whose target type isn't in the input set are silently dropped
/// — the referenced module isn't loaded, so the dependency is moot. Constraints
/// whose target type doesn't implement <see cref="IKoanInitializer"/> throw
/// (a definite mistake worth surfacing at startup, not at first request).
/// Cycles throw with the cycle path rendered for the operator.
/// </para>
/// </remarks>
public static class RegistrarOrdering
{
    /// <summary>
    /// Sort the supplied types in a stable, constraint-satisfying order.
    /// </summary>
    /// <param name="types">Candidate types. Duplicates are de-duplicated.</param>
    /// <returns>Sorted array, same membership as <paramref name="types"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a constraint target isn't assignable to
    /// <see cref="IKoanInitializer"/>, or when the constraint graph contains
    /// a cycle. The message names the offending edges / cycle path.
    /// </exception>
    public static Type[] Sort(IEnumerable<Type> types)
    {
        ArgumentNullException.ThrowIfNull(types);

        var nodes = types.Where(t => t is not null).Distinct().ToArray();
        if (nodes.Length == 0) return nodes;
        // NOTE: don't short-circuit on length == 1 — even single-node inputs
        // need attribute validation so a registrar with [Before(self)] or
        // [After(typeof(string))] fails loudly at startup rather than
        // silently no-oping.

        var nodeSet = new HashSet<Type>(nodes);

        // Build edges: 'from -> to' means "from must run before to".
        // [Before(T)] on A adds A -> T. [After(T)] on A adds T -> A.
        // Use a set so duplicate edges (same constraint declared from both
        // sides) collapse to one.
        var edges = new HashSet<(Type From, Type To)>();
        foreach (var node in nodes)
        {
            foreach (var attr in node.GetCustomAttributes(typeof(BeforeAttribute), inherit: false))
            {
                if (attr is not BeforeAttribute before) continue;
                foreach (var target in before.Targets)
                {
                    ValidateTarget(node, target, "[Before]");
                    if (!nodeSet.Contains(target)) continue; // module not loaded; constraint moot
                    if (target == node) ThrowSelfReference(node, "[Before]");
                    edges.Add((node, target));
                }
            }
            foreach (var attr in node.GetCustomAttributes(typeof(AfterAttribute), inherit: false))
            {
                if (attr is not AfterAttribute after) continue;
                foreach (var target in after.Targets)
                {
                    ValidateTarget(node, target, "[After]");
                    if (!nodeSet.Contains(target)) continue;
                    if (target == node) ThrowSelfReference(node, "[After]");
                    edges.Add((target, node));
                }
            }
        }

        if (edges.Count == 0)
        {
            // No constraints anywhere: degenerate to a stable AQN sort. This
            // is strictly more deterministic than the previous behavior.
            Array.Sort(nodes, CompareByAssemblyQualifiedName);
            return nodes;
        }

        // Kahn's algorithm. The 'ready' set is a SortedSet keyed by AQN so
        // ties are broken deterministically — without it, the order among
        // unconstrained-but-simultaneously-ready nodes would still be
        // dependent on enumeration of the input array.
        var inDegree = nodes.ToDictionary(n => n, _ => 0);
        foreach (var (_, to) in edges) inDegree[to]++;

        var ready = new SortedSet<Type>(AssemblyQualifiedNameComparer.Instance);
        foreach (var n in nodes)
        {
            if (inDegree[n] == 0) ready.Add(n);
        }

        var sorted = new List<Type>(nodes.Length);
        // Precompute adjacency for fast removal.
        var outgoing = edges
            .GroupBy(e => e.From)
            .ToDictionary(g => g.Key, g => g.Select(e => e.To).ToArray());

        while (ready.Count > 0)
        {
            var next = ready.Min!;
            ready.Remove(next);
            sorted.Add(next);

            if (!outgoing.TryGetValue(next, out var dependents)) continue;
            foreach (var d in dependents)
            {
                inDegree[d]--;
                if (inDegree[d] == 0) ready.Add(d);
            }
        }

        if (sorted.Count != nodes.Length)
        {
            var residual = nodes.Where(n => !sorted.Contains(n)).ToArray();
            ThrowCycle(residual, edges);
        }

        return sorted.ToArray();
    }

    private static void ValidateTarget(Type source, Type target, string attributeName)
    {
        if (target is null)
        {
            throw new InvalidOperationException(
                $"Koan ordering: {attributeName} on '{source.FullName}' has a null target.");
        }
        if (!typeof(IKoanInitializer).IsAssignableFrom(target))
        {
            throw new InvalidOperationException(
                $"Koan ordering: {attributeName}({target.FullName}) on '{source.FullName}' is invalid — " +
                $"target does not implement {nameof(IKoanInitializer)}.");
        }
    }

    private static void ThrowSelfReference(Type node, string attributeName)
    {
        throw new InvalidOperationException(
            $"Koan ordering: {attributeName} on '{node.FullName}' references itself.");
    }

    private static void ThrowCycle(Type[] residual, HashSet<(Type From, Type To)> edges)
    {
        // Recover a representative cycle path from the residual subgraph by
        // walking forward until we revisit a node. Works because every node
        // in the residual set has in-degree > 0 (otherwise Kahn would have
        // consumed it), so the walk is guaranteed to revisit.
        var residualSet = new HashSet<Type>(residual);
        var outgoing = edges
            .Where(e => residualSet.Contains(e.From) && residualSet.Contains(e.To))
            .GroupBy(e => e.From)
            .ToDictionary(g => g.Key, g => g.Select(e => e.To).ToArray());

        var path = new List<Type>();
        var seen = new HashSet<Type>();
        var current = residual.OrderBy(t => t.AssemblyQualifiedName, StringComparer.Ordinal).First();
        while (seen.Add(current))
        {
            path.Add(current);
            if (!outgoing.TryGetValue(current, out var next) || next.Length == 0) break;
            current = next[0];
        }
        path.Add(current); // closes the loop visually

        var cyclePath = string.Join(" -> ", path.Select(t => t.FullName));
        throw new InvalidOperationException(
            "Koan ordering: cycle detected in [Before]/[After] constraints. " +
            $"Cycle path: {cyclePath}. Resolve by removing one of the constraints along the cycle.");
    }

    private static int CompareByAssemblyQualifiedName(Type x, Type y)
        => string.Compare(x.AssemblyQualifiedName, y.AssemblyQualifiedName, StringComparison.Ordinal);

    private sealed class AssemblyQualifiedNameComparer : IComparer<Type>
    {
        public static readonly AssemblyQualifiedNameComparer Instance = new();
        public int Compare(Type? x, Type? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            return string.Compare(x.AssemblyQualifiedName, y.AssemblyQualifiedName, StringComparison.Ordinal);
        }
    }
}
