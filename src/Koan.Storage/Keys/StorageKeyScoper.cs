using System;
using System.Collections.Generic;
using System.Globalization;
using Koan.Core.Hosting.App;
using Koan.Core.Naming;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Storage.Keys;

/// <summary>
/// STOR-0011 §2–§4: guard then compose a LOGICAL blob key into a PHYSICAL (axis-prefixed) key. Called by
/// <see cref="ScopedStorageService"/> on every op. Three paths, decided by <see cref="StorageScope"/>:
/// an explicit host scope is unprefixed + unguarded; a typed scope uses <c>ManagedFieldRegistry.ForType</c> +
/// the typed <c>IStorageGuard</c> (data-path parity, with the <c>[HostScoped]</c> exemption); a type-less raw
/// caller falls back to the ambient axis bag + a value-based guard (fail-safe). Off (no axis / no guard) ⇒ the
/// key is returned unchanged (byte-identical).
/// </summary>
public static class StorageKeyScoper
{
    private const string Sep = "/";
    private static readonly CompositionPolicy Policy = new(Sep, StorageKeyParticleFormatter.Instance);

    /// <summary>Returns the physical key. Throws (fail-closed) if a registered guard blocks the op.</summary>
    public static string Scope(string logicalKey)
    {
        ArgumentNullException.ThrowIfNull(logicalKey);
        if (StorageScope.IsHostScope) return logicalKey;          // explicit host scope ⇒ unprefixed, unguarded

        var type = StorageScope.CurrentType;
        return type is not null
            ? ScopeTyped(type, logicalKey)                        // type-aware path (data-path parity)
            : ScopeAmbient(logicalKey);                           // fail-safe: raw IStorageService caller
    }

    private static string ScopeTyped(Type type, string logicalKey)
    {
        RunGuards(type);                                          // typed guard ([HostScoped] exemption is inside it)
        if (ManagedFieldRegistry.IsEmpty) return logicalKey;      // off ⇒ byte-identical
        var managed = ManagedFieldRegistry.ForType(type);
        List<Particle>? ps = null;
        for (var i = 0; i < managed.Count; i++)
        {
            var d = managed[i];
            var v = d.ValueProvider();
            if (!d.AutoReadFilter)                                // §3: a non-equality axis is never a path segment
            {
                if (v is not null)
                    throw new InvalidOperationException(
                        $"Non-equality axis '{d.StorageName}' cannot scope a blob key — storage is equality-only (STOR-0011 §3).");
                continue;
            }
            var s = Convert.ToString(v, CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(s)) continue;                // the guard is the no-scope authority
            (ps ??= new List<Particle>(managed.Count)).Add(new Particle(i, d.StorageName, s, ParticlePosition.Leading, Sep));
        }
        return ps is null ? logicalKey : IdentifierComposer.Compose(logicalKey, ps.ToArray(), Policy);
    }

    private static string ScopeAmbient(string logicalKey)
    {
        var sp = AppHost.Current;
        if (sp is not null)
            // typeof(object) sentinel = "unknown type, treat as scope-required"; TenantStorageGuard fails closed
            // when no concrete tenant is in scope (the unscoped-raw-op fail-safe). Run BEFORE the IsEmpty
            // short-circuit so a guard registered without a managed field (a future predicate-only axis) still
            // fires — parity with ScopeTyped (which guards unconditionally).
            foreach (var g in sp.GetServices<IStorageGuard>()) g.Guard(typeof(object));
        if (ManagedFieldRegistry.IsEmpty) return logicalKey;      // off ⇒ byte-identical (no axis)
        // A raw IStorageService caller has no entity type, so there is no [HostScoped] exemption (a type-less op
        // cannot be host-scoped — isolate by default). Read EVERY registered equality axis's ambient value (no
        // AppliesTo filter). Use the descriptor's CLEAN ValueProvider value — NOT AmbientCarrierRegistry.Capture(),
        // whose value is a versioned durable-restore token (e.g. "v1:id:acme"), not a path-clean segment.
        var all = ManagedFieldRegistry.All;
        List<Particle>? ps = null;
        for (var i = 0; i < all.Count; i++)
        {
            var d = all[i];
            if (!d.AutoReadFilter)                                // §3: a non-equality axis is never a path segment...
            {
                if (d.ValueProvider() is not null)               // ...and a value-yielding one FAILS CLOSED (not dropped)
                    throw new InvalidOperationException(
                        $"Non-equality axis '{d.StorageName}' cannot scope a blob key — storage is equality-only (STOR-0011 §3).");
                continue;
            }
            var s = Convert.ToString(d.ValueProvider(), CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(s)) continue;
            (ps ??= new List<Particle>(all.Count)).Add(new Particle(i, d.StorageName, s, ParticlePosition.Leading, Sep));
        }
        return ps is null ? logicalKey : IdentifierComposer.Compose(logicalKey, ps.ToArray(), Policy);
    }

    private static void RunGuards(Type type)
    {
        var sp = AppHost.Current;
        if (sp is null) return;
        foreach (var g in sp.GetServices<IStorageGuard>()) g.Guard(type);
    }
}
