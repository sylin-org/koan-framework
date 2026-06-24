using Koan.Core.Capabilities;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Core.Pipeline;

/// <summary>
/// A generic, axis-agnostic <b>read-scoping seam</b> (DATA-0106) — the predicate-shaped sibling of
/// <see cref="IStorageGuard"/> and the <c>ManagedFieldDescriptor</c> write-stamp. The <c>RepositoryFacade</c> folds
/// every registered contributor's predicate into every read (Query/Count and the key-op IDOR lowering) of an entity.
/// A cross-cutting module returns a <see cref="Filter"/> that scopes what the current ambient may see — tenancy's
/// scalar equality (the built-in default, <c>ManagedEqualityReadContributor</c>), or a future moderation capability's
/// <b>non-equality</b> row-visibility predicate (<c>Filter.AnyOf(...)</c>, <c>Filter.Ne(...)</c>). The data core never
/// names the axis; no registered contributor ⇒ the fold is empty ⇒ no-op (structural absence; Reference = Intent).
///
/// <para>This is <b>data, not behaviour</b> (descriptor-not-callback, [koan-design-principles]): the contributor
/// returns an immutable <see cref="Filter"/> + declares an isolation <see cref="RequiredCapability"/>; it has no
/// channel to throw. The <b>facade owns fail-closed</b> (DATA-0106 §4): a contributor that yields a filter on an
/// adapter that cannot announce the capability — or cannot push the predicate at the store — fails closed there.</para>
/// </summary>
public interface IReadFilterContributor
{
    /// <summary>
    /// The predicate to AND-fold into every read of <paramref name="entityType"/>, or <c>null</c> when this
    /// contributor imposes no constraint in the current ambient (off / host / not-applicable). Must be cheap — it
    /// runs on every read; cache per-type metadata.
    /// </summary>
    Filter? ReadFilter(System.Type entityType);

    /// <summary>
    /// The isolation capability this contributor needs the adapter to announce (the same nullable
    /// <see cref="Capability"/> type <c>ManagedFieldDescriptor.RequiredCapability</c> uses). When set, the facade
    /// fails closed if an active read on a non-announcing adapter occurs. <c>null</c> is <b>not</b> a free pass: the
    /// facade still requires the folded filter to be fully pushable at the store (bias-to-strict, DATA-0106 §4). The
    /// built-in equality contributor returns <c>null</c> here — its per-descriptor capabilities are enforced by the
    /// facade's managed-descriptor inspection.
    /// </summary>
    Capability? RequiredCapability { get; }

    /// <summary>
    /// Does this contributor impose a read scope on <paramref name="entityType"/> that CANNOT be expressed as an
    /// equality cache-key segment — a viewer-context / NON-equality predicate (DATA-0106 §5)? An id-keyed cache namespace
    /// is equality-by-construction, so such an entity must be EXCLUDED from caching entirely (the cache decorates outside
    /// the read-filter chokepoint; a cache hit would serve a row the predicate hides). This is <b>ambient-independent</b>
    /// — a declared property of the axis, not derived from the current scope — so the cache can consult it at decorator
    /// construction (where there is no ambient). The built-in equality contributor returns <c>false</c> (its scope IS a
    /// cache-key segment, partitioned via the managed-scope suffix); a predicate axis returns <c>true</c> for the types
    /// it scopes. Default-implemented to <c>false</c> so an equality-shaped contributor need not override it.
    /// </summary>
    bool ExcludesFromCache(System.Type entityType) => false;
}
