using System;
using System.Linq;

namespace Koan.Data.Core.Routing;

/// <summary>
/// The boot-time index of <see cref="DatabaseRouteDescriptor"/>s (ARCH-0102 §3 — the Database-mode auto-routing plane).
/// A Database-mode <c>[DataAxis]</c> registers its route from <c>DataAxisExpander</c> (Reference = Intent); the data
/// core's <c>AdapterResolver</c> reads it generically — after an explicit <c>EntityContext.Source</c> override — and
/// derives the data source from the ambient, never naming the axis.
///
/// <para>Deliberate static index (not DI) — the same declared deviation as <c>OperationOverrideRegistry</c> /
/// <c>ManagedFieldRegistry</c>. <b>Off = byte-identical:</b> when no Database-mode axis is registered <see cref="IsEmpty"/>
/// is <c>true</c>, so the resolver hot path is a single volatile reference read and the priority chain is unchanged —
/// this is the load-bearing non-regression gate (FC-5).</para>
///
/// <para><b>Copy-on-write</b> — routes are immutable after boot, so the runtime path needs no lock: <c>Register</c>
/// publishes a fresh array under the gate, <see cref="ResolveSourceKey"/> takes a single volatile snapshot of the
/// reference and iterates it lock-free. The user-supplied <c>SourceKeyProvider</c> delegate is therefore invoked
/// <i>outside</i> any lock (no re-entrancy deadlock, no serializing concurrent resolutions). <c>ResolveSourceKey</c>
/// reads the ambient on every call by design: the repository facade caches per resolved (entity, adapter, source), so a
/// per-call ambient change routes to a distinct (correct) source.</para>
/// </summary>
public static class DatabaseRouteRegistry
{
    private static readonly object _gate = new();
    private static volatile DatabaseRouteDescriptor[] _routes = Array.Empty<DatabaseRouteDescriptor>();

    /// <summary>Whether no Database-mode route is registered — the hot-path off gate (FC-5). A single volatile read.</summary>
    public static bool IsEmpty => _routes.Length == 0;

    /// <summary>Register a Database-mode route. Boot-only. Idempotent by <see cref="DatabaseRouteDescriptor.AxisId"/>
    /// (a re-entrant re-expansion across hosts is a no-op). Publishes a fresh array so concurrent readers see a stable snapshot.</summary>
    public static void Register(DatabaseRouteDescriptor route)
    {
        ArgumentNullException.ThrowIfNull(route);
        lock (_gate)
        {
            if (_routes.Any(r => string.Equals(r.AxisId, route.AxisId, StringComparison.Ordinal))) return;
            var next = new DatabaseRouteDescriptor[_routes.Length + 1];
            Array.Copy(_routes, next, _routes.Length);
            next[_routes.Length] = route;
            _routes = next;   // volatile publish — a reader either sees the old or the new array, never a torn one
        }
    }

    /// <summary>
    /// The source name an entity's Database-mode axis routes to right now, or <c>null</c> when none applies (no Database
    /// axis governs the type, or its ambient source-key is unset). The first applicable route with a non-blank key wins.
    /// Lock-free: snapshots the route array reference once, then invokes the (user-supplied) predicates/providers off-lock.
    /// </summary>
    public static string? ResolveSourceKey(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        var routes = _routes;   // single volatile read = a stable snapshot for this resolution
        foreach (var route in routes)
        {
            if (!route.AppliesTo(entityType)) continue;
            var raw = route.SourceKeyProvider();
            var key = raw as string ?? raw?.ToString();
            if (!string.IsNullOrWhiteSpace(key)) return key;
        }
        return null;
    }

    /// <summary>Test-support: clear all registrations (mirrors <c>OperationOverrideRegistry.Reset</c>).</summary>
    public static void Reset()
    {
        lock (_gate) _routes = Array.Empty<DatabaseRouteDescriptor>();
    }
}
