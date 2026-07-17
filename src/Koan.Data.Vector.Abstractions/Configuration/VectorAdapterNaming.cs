using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core.Logging;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core.Routing;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector.Abstractions.Configuration;

/// <summary>
/// Resolves vector collection/index identifiers via the shared <see cref="StorageNameGenerator"/>, folding BOTH AODB
/// isolation discriminators into the name (the name-mangling floor every vector store realizes uniformly): the ambient
/// <b>partition</b> (Container mode) and the Database-mode routed <b>source</b> (ARCH-0103 §6). A distinct partition
/// resolves to a distinct physical collection (Container); a distinct routed source resolves to a distinct physical
/// collection on the same store (Database). Both off (no partition, source = Default) ⇒ byte-identical to the prior name.
///
/// <para>The factory still owns the naming <b>capability</b> (charset / separators / limit) via
/// <see cref="INamingProvider.GetNamingCapability"/>; this routes through the source-aware
/// <see cref="StorageNameGenerator.Resolve(string,Type,string?,string?,Func{StorageNamingCapability})"/> overload so the
/// source is folded with the SAME identifier-safe rendering as the partition (the record-plane <c>ResolveStorage</c>
/// stays source-free — the record plane routes a source to a distinct physical store via the factory, not the name).</para>
/// </summary>
public static class VectorAdapterNaming
{
    public static string GetOrCompute<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var cfg = VectorConfigs.Get<TEntity, TKey>(sp);
        var factory = sp.GetRequiredService<IVectorProviderResolver>().Find(cfg.Provider)
            ?? throw new InvalidOperationException($"No vector adapter factory for provider '{cfg.Provider}'.");
        // ARCH-0103: the SAME routed source the record plane + VectorService resolve (explicit EntityContext.Source >
        // Database-mode axis route > null). Folded into the name so the vector plane physically isolates per source.
        var source = RoutedSource.Resolve<TEntity>().Source;
        return StorageNameGenerator.Resolve(
            factory.Provider, typeof(TEntity), Koan.Data.Core.EntityContext.Current?.Partition, source,
            () => factory.GetNamingCapability(sp));
    }

    // ARCH-0103 §6 follow-on — the CollectionName/IndexName pin footgun. An adapter option that pins a STATIC
    // collection/index name (Qdrant/Milvus CollectionName, ES/OpenSearch IndexName) is used verbatim, bypassing the
    // name-fold above — so the THREE isolation discriminators that fold IS the name are all DEFEATED for that entity (every
    // value collapses onto the one pinned name): (1) the ambient partition particle, (2) the Database-mode routed source
    // particle, and (3) container-name particles from a Container-mode [DataAxis] (StorageNameParticleRegistry — the
    // "a separate container IS the isolation" realization). Tenancy (RowScoped) is unaffected: it isolates by a metadata
    // read-filter, not the name. The pin is a deliberate, documented override, so this WARNS rather than fails closed —
    // but only when it actually bites (one of the three discriminators is in scope), and once per entity type.
    private static readonly ConcurrentDictionary<Type, byte> _pinWarned = new();
    private static readonly KoanLog.KoanLogScope Log = KoanLog.For(typeof(VectorAdapterNaming));

    /// <summary>
    /// True when <paramref name="pinnedName"/> is a non-blank static name AND an isolation discriminator that the pin
    /// bypasses is in scope for <typeparamref name="TEntity"/> — mirroring exactly what
    /// <see cref="StorageNameGenerator.Resolve(string,Type,string?,string?,Func{StorageNamingCapability})"/> folds into
    /// the name: an ambient partition (Container), a routed Database source (Database, explicit or axis-derived), or a
    /// Container-mode <c>[DataAxis]</c> container-name particle (<see cref="StorageNameParticleRegistry"/>). Pure (no
    /// state); the basis of <see cref="WarnIfPinnedNameDefeatsIsolation{TEntity}"/>. A blank pin (the "no override"
    /// sentinel) and the no-discriminator case both return false (the pin is harmless there).
    /// </summary>
    public static bool PinnedNameDefeatsActiveIsolation<TEntity>(string? pinnedName) where TEntity : class
        => !string.IsNullOrWhiteSpace(pinnedName) && ActiveNameFoldDiscriminator<TEntity>() is not null;

    /// <summary>
    /// The name of the isolation discriminator the name-fold would apply for <typeparamref name="TEntity"/> right now, or
    /// null if none is in scope. Checked in the same order the name composes (partition, then a Container-axis particle,
    /// then the routed source); each branch is gated so the off path is a couple of cheap ambient/volatile reads.
    /// </summary>
    private static string? ActiveNameFoldDiscriminator<TEntity>() where TEntity : class
    {
        if (!string.IsNullOrEmpty(Koan.Data.Core.EntityContext.Current?.Partition)) return "partition";        // Container (partition)
        if (!StorageNameParticleRegistry.IsEmpty && StorageNameParticleRegistry.Gather(typeof(TEntity)).Length > 0)
            return "container-particle";                                                                       // Container ([DataAxis])
        return RoutedSource.Resolve<TEntity>().Kind != RouteKind.None ? "source" : null;                       // Database (routed source)
    }

    /// <summary>
    /// Emit a one-per-entity-type config warning when a pinned <paramref name="optionName"/> (<paramref name="pinnedName"/>)
    /// would defeat the active partition/container-particle/source isolation fold for <typeparamref name="TEntity"/>.
    /// Adapters call this from the pinned branch of their name resolver. Off (no pin, or no discriminator in scope) ⇒ no
    /// emission ⇒ byte-identical; the already-warned fast path keeps the hot name-resolution path cheap after the first.
    /// </summary>
    public static void WarnIfPinnedNameDefeatsIsolation<TEntity>(string pinnedName, string optionName) where TEntity : class
    {
        if (_pinWarned.ContainsKey(typeof(TEntity))) return;                       // already warned ⇒ cheapest path
        if (string.IsNullOrWhiteSpace(pinnedName)) return;                         // not actually pinned
        var discriminator = ActiveNameFoldDiscriminator<TEntity>();
        if (discriminator is null) return;                                         // pin harmless for this op
        if (!_pinWarned.TryAdd(typeof(TEntity), 0)) return;                        // lost the race ⇒ another thread warned
        Log.ConfigWarning("vector.name.pinned", "isolation-defeated",
            ("entity", typeof(TEntity).Name),
            ("option", optionName),
            ("pinnedName", pinnedName),
            ("activeDiscriminator", discriminator),
            ("note", $"the pinned {optionName} bypasses the partition/source/container-axis name-fold, so Container/Database isolation is defeated for this entity (all partitions/sources share one {optionName}); remove the pin to restore isolation"));
    }
}
