using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Core.Axes;

/// <summary>
/// The <c>RepositoryFacade</c>'s read-scope inspection, exposed <b>non-throwing</b> (ARCH-0101 §9) so
/// <see cref="DataAxis.Explain"/> (the query-RSoP) reads the ONE inspection authority the facade already computed at
/// construction — never re-deriving the capability/pushdown logic. It is the same seam the §8 boot-refuses-leaky-axis
/// pre-flight builds on. Resolved via <c>IDataService.GetScopeDiagnostics</c> (the undecorated facade — the authority
/// that holds the RAW adapter for the <c>IQueryRepository</c> check). All members are cheap, side-effect-free, and safe
/// to call at boot (capability description is static — no store connection).
/// </summary>
public interface IAxisScopeDiagnostics
{
    /// <summary>The inner adapter's type name (e.g. <c>"SqliteRepository`2"</c>) — for the report / fix-it message.</summary>
    string AdapterName { get; }

    /// <summary>Whether this entity could EVER be scoped (it has a managed field or a non-default read contributor).
    /// <c>false</c> ⇒ the byte-identical no-op path (no axis touches it).</summary>
    bool CouldScope { get; }

    /// <summary>Whether the adapter satisfies the isolation contract for this entity — it announces every required
    /// isolation capability (over the managed descriptors AND the read contributors) and is an <c>IQueryRepository</c>.
    /// <c>true</c> when the entity could not be scoped at all (nothing to satisfy).</summary>
    bool ScopeAdapterOk { get; }

    /// <summary>The fail-closed fix-it message when the adapter cannot isolate a scoped op, else <c>null</c>.</summary>
    string? ScopeAdapterError { get; }

    /// <summary>The read-scope predicate active in the CURRENT ambient (the AND-fold of every contributor), or
    /// <c>null</c> when nothing scopes this entity right now (off / host). Does NOT fail closed (diagnostic only).</summary>
    Filter? CurrentReadScope();

    /// <summary>Whether <paramref name="folded"/> is fully pushable at the store (no in-memory residual) — the §4b
    /// isolation-must-push-down check, read non-throwing.</summary>
    bool IsFullyPushable(Filter folded);
}
