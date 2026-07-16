using System;
using System.Collections.Generic;
using Koan.Cache.Abstractions.Primitives;
using Koan.Core.Naming;
using Koan.Data.Abstractions.Pipeline;

namespace Koan.Cache.Keys;

/// <summary>
/// The one canonical builder for a <b>scoped</b> entity cache key (redesign gap B,
/// <c>docs/architecture/cache-scope-key-convergence.md</c>). The host-owned Entity cache plan formats the selected
/// policy template once and delegates managed equality-axis folding here, so repository reads/writes and explicit
/// <c>entity.Cache.Evict()</c> operations agree by construction.
///
/// <para>The scope segment is the set of <b>equality</b> managed axes (e.g. the tenant <c>__koan_tenant</c>
/// discriminator) applicable to the entity, folded through the ONE ARCH-0096 <see cref="AmbientAxisComposer"/> —
/// the same engine that composes job-coalesce and storage-name identifiers — so the cache renders an axis
/// identically to every other pillar (DATA-0105 §3.2: the managed scope MUST partition the key because the cache
/// decorates OUTSIDE the facade's read-filter). A non-equality axis
/// (<see cref="ManagedFieldDescriptor.AutoReadFilter"/> == <c>false</c>) is never a cache-key segment — those
/// entities are cache-excluded entirely (DATA-0106 §5). Off / no managed field applicable ⇒ the base key is
/// returned unchanged (0-alloc fast path, byte-identical to the bare key).</para>
/// </summary>
internal static class ScopedEntityCacheKey
{
    private const string ScopeSeparator = "::";

    /// <summary>
    /// Fold the managed equality-axis scope onto an <b>already-formatted</b> base key — the read-path entry point.
    /// <c>CachedRepository</c> formats the base from the (possibly custom) policy template and delegates only the
    /// scope-fold here, so a custom <c>[CachePolicy]</c> key template is preserved.
    /// </summary>
    public static string AppendScope(string baseKey, Type entityType)
    {
        ArgumentNullException.ThrowIfNull(baseKey);
        ArgumentNullException.ThrowIfNull(entityType);
        var scope = BuildScopeBag(entityType);
        return scope is null
            ? baseKey
            : AmbientAxisComposer.Append(baseKey, scope, ParticlePosition.Trailing, ScopeSeparator);
    }

    // The equality managed axes applicable to the type, as an axis bag { StorageName -> ValueProvider() }. Returns
    // null when none apply (the off / no-axis fast path) so AppendScope returns the base unchanged. The equality
    // SELECTION is the ONE shared ManagedFieldRegistry.EqualityFields (a non-equality axis already excludes the whole
    // type from caching, CachedRepository._excludeFromCache, so it never reaches here); this consumer only renders.
    private static IReadOnlyDictionary<string, string>? BuildScopeBag(Type entityType)
    {
        var managed = ManagedFieldRegistry.EqualityFields(entityType);
        if (managed.Count == 0) return null;

        Dictionary<string, string>? bag = null;
        for (var i = 0; i < managed.Count; i++)
            (bag ??= new Dictionary<string, string>(StringComparer.Ordinal))[managed[i].StorageName]
                = managed[i].ValueProvider()?.ToString() ?? "_";
        return bag;
    }
}
