using System.Collections.Generic;
using System.Linq;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Core.Axes;

/// <summary>
/// The query-RSoP (Resultant Set of Policy) for an entity (ARCH-0101 §9) — the honest, inspectable projection of its
/// whole isolation story in the current ambient: the composing <see cref="Planes"/>, the active <see cref="ReadScope"/>
/// predicate, the adapter's fail-closed <see cref="Adapter"/> satisfaction, and whether the entity is cache-excluded.
/// </summary>
public sealed record AxisExplanation(
    System.Type EntityType,
    IReadOnlyList<AxisPlane> Planes,
    Filter? ReadScope,
    bool CacheExcluded,
    AxisAdapter? Adapter)
{
    /// <summary>Whether a read of this entity is scoped by some axis right now (the fold is non-empty).</summary>
    public bool ReadScopedNow => ReadScope is not null;

    /// <summary>
    /// Whether this is a <b>leak in waiting</b> — the entity is read-scoped now (or could be) but the adapter cannot
    /// satisfy the isolation: it does not announce a required capability, is not an <c>IQueryRepository</c>, the active
    /// predicate cannot be fully pushed down, or its configured route cannot be resolved. A scoped op on it fails closed
    /// at runtime; this is the signal the §8 boot-refuses-leaky-axis pre-flight uses to refuse a leaky boot.
    /// </summary>
    public bool IsLeak => Adapter is { IsolationSatisfied: false }
        || (ReadScopedNow && Adapter is { Pushable: false });

    /// <summary>A one-line human summary for the boot report / logs.</summary>
    public string Summary()
    {
        var planeNote = Planes.Count == 0 ? "no axes" : string.Join(", ", Planes.Select(p => $"{p.Plane}:{p.Key}"));
        var scope = ReadScopedNow ? "scoped-now" : "unscoped-now";
        var adapterNote = Adapter is null ? "" :
            $"; adapter={Adapter.Name} {(Adapter.IsolationSatisfied ? "isolates" : "CANNOT-ISOLATE")}" +
            (Adapter.Pushable is { } push ? $"; predicate {(push ? "pushes-down" : "NOT-PUSHABLE")}" : "");
        return $"{EntityType.Name}: {planeNote}; {scope}; {(CacheExcluded ? "cache-excluded" : "cacheable")}{adapterNote}";
    }
}

/// <summary>One composing plane in an <see cref="AxisExplanation"/> — its kind, identifying key, and a human detail.</summary>
/// <param name="Plane">The plane kind: <c>managed-field</c> / <c>read-filter</c> / <c>name-particle</c> / <c>operation-override</c>.</param>
/// <param name="Key">The plane's identifying key — the field name, axis id, particle axis, or override field.</param>
/// <param name="Detail">A human-readable detail (capability, indexed, active-now, cache state, …).</param>
public sealed record AxisPlane(string Plane, string Key, string Detail);

/// <summary>The adapter's fail-closed satisfaction for a scoped entity (ARCH-0101 §8).</summary>
/// <param name="Name">The inner adapter's type name.</param>
/// <param name="IsolationSatisfied">Whether the adapter announces every required isolation capability and is an <c>IQueryRepository</c>.</param>
/// <param name="FailClosedReason">The fix-it message when it cannot isolate, else <c>null</c>.</param>
/// <param name="Pushable">Whether the active read-scope predicate fully pushes down (no in-memory residual); <c>null</c> when nothing scopes now.</param>
public sealed record AxisAdapter(string Name, bool IsolationSatisfied, string? FailClosedReason, bool? Pushable);
