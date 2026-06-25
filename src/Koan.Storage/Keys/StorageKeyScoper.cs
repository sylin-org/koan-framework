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
        // §3: storage encodes only EQUALITY axes (a blob path is equality-by-construction); a value-yielding
        // non-equality axis fails closed (storage cannot enforce a viewer-context predicate in a key).
        RefuseNonEqualityScope(ManagedFieldRegistry.ForType(type));
        // The equality SELECTION is the ONE shared ManagedFieldRegistry.EqualityFields (no re-derivation); this
        // consumer only renders the leading path particle.
        return ComposeEqualityParticles(ManagedFieldRegistry.EqualityFields(type), logicalKey);
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
        // A raw IStorageService caller has no entity type, so there is no [HostScoped] exemption nor AppliesTo
        // filter (a type-less op cannot be host-scoped — isolate by default). Use the descriptor's CLEAN
        // ValueProvider value — NOT AmbientCarrierRegistry.Capture(), whose value is a versioned durable-restore
        // token (e.g. "v1:id:acme"), not a path-clean segment.
        var all = ManagedFieldRegistry.All;
        RefuseNonEqualityScope(all);
        return ComposeEqualityParticles(all.Where(static d => d.AutoReadFilter).ToArray(), logicalKey);
    }

    // §3 fail-closed: a value-yielding non-equality axis cannot be a blob-key segment.
    private static void RefuseNonEqualityScope(IReadOnlyList<ManagedFieldDescriptor> managed)
    {
        for (var i = 0; i < managed.Count; i++)
            if (!managed[i].AutoReadFilter && managed[i].ValueProvider() is not null)
                throw new InvalidOperationException(
                    $"Non-equality axis '{managed[i].StorageName}' cannot scope a blob key — storage is equality-only (STOR-0011 §3).");
    }

    // Render the equality descriptors' values as leading path particles (the one composition; the guard is the
    // no-scope authority, so a null/empty value is simply omitted).
    private static string ComposeEqualityParticles(IReadOnlyList<ManagedFieldDescriptor> equality, string logicalKey)
    {
        List<Particle>? ps = null;
        for (var i = 0; i < equality.Count; i++)
        {
            var s = Convert.ToString(equality[i].ValueProvider(), CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(s)) continue;
            (ps ??= new List<Particle>(equality.Count)).Add(new Particle(i, equality[i].StorageName, s, ParticlePosition.Leading, Sep));
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
