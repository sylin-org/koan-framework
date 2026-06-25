using System;

namespace Koan.Data.Core.Routing;

/// <summary>How a <see cref="RoutedSource"/> was determined — distinguishes the caller's explicit override from an
/// automatic Database-mode route, so a consumer can fail closed with the right message (ARCH-0103 §4.1).</summary>
public enum RouteKind
{
    /// <summary>No source is in scope — the resolver falls through to its normal chain (the common, off-axis case).</summary>
    None,

    /// <summary>An explicit <c>EntityContext.Source</c> — the caller's deliberate override, which always wins.</summary>
    Explicit,

    /// <summary>A <see cref="Koan.Data.Core.Axes.AxisMode.Database"/> <c>[DataAxis]</c> derived the source from the ambient.</summary>
    DatabaseAxis,
}

/// <summary>
/// The <b>one routing decision</b> both data planes share (ARCH-0103 §4.1 — the Moniker contract): given the ambient
/// context, which named data source does this entity's operation route to, and was that choice explicit or
/// axis-derived. An explicit <c>EntityContext.Source</c> wins; else a Database-mode <c>[DataAxis]</c> route
/// (<see cref="DatabaseRouteRegistry"/>); else none. <c>AdapterResolver</c> (record) and <c>VectorService</c> (vector)
/// both resolve through this, so a Database-mode axis routes <em>both</em> planes to the same source — the
/// vector/record split-brain is closed by construction.
/// </summary>
/// <remarks>
/// <b>Off = byte-identical:</b> when no explicit source is set and <see cref="DatabaseRouteRegistry.IsEmpty"/> is true,
/// <see cref="Resolve(Type, Koan.Data.Core.EntityContext.ContextState?)"/> is a single ambient read plus a volatile
/// reference read and returns <see cref="RouteKind.None"/> — the load-bearing non-regression gate (FC-5). The check
/// order (explicit Source, then the Database route) is identical to the pre-ARCH-0103 <c>AdapterResolver</c> chain.
/// </remarks>
public readonly record struct RoutedSource(string? Source, RouteKind Kind)
{
    /// <summary>Resolve the routed source for <typeparamref name="TEntity"/> from the current ambient context.</summary>
    public static RoutedSource Resolve<TEntity>() where TEntity : class
        => Resolve(typeof(TEntity), EntityContext.Current);

    /// <summary>
    /// Resolve the routed source for <paramref name="entityType"/> against a pre-snapshotted ambient
    /// <paramref name="ctx"/> — the overload <c>AdapterResolver</c> uses so the ambient is read exactly once on the
    /// hot path (hot-path discipline, [[koan-design-principles]]).
    /// </summary>
    public static RoutedSource Resolve(Type entityType, EntityContext.ContextState? ctx)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        // Priority 1 — an explicit EntityContext.Source is the caller's deliberate override and always wins.
        if (!string.IsNullOrWhiteSpace(ctx?.Source))
            return new RoutedSource(ctx.Source, RouteKind.Explicit);

        // Priority 1.5 (ARCH-0102 §3) — a Database-mode axis derives the source from the ambient (auto-routing).
        // Gated by IsEmpty: when no Database-mode axis is registered this is a single volatile read (off = byte-identical).
        if (!DatabaseRouteRegistry.IsEmpty)
        {
            var key = DatabaseRouteRegistry.ResolveSourceKey(entityType);
            if (key is not null) return new RoutedSource(key, RouteKind.DatabaseAxis);
        }

        return new RoutedSource(null, RouteKind.None);
    }
}
