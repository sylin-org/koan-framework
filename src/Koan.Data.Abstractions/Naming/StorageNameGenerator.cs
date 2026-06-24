using System.Collections.Concurrent;
using System.Text;
using Koan.Core.Naming;

namespace Koan.Data.Abstractions.Naming;

/// <summary>
/// Single source of truth that turns an <c>(entityType, partition)</c> pair into a physical storage
/// identifier, honoring an adapter's announced <see cref="StorageNamingCapability"/>. Adapters announce
/// their capability; the framework owns the algorithm — so name generation can never drift between adapters.
/// The composition (anchor + partition particle + container-name particles + byte clamp) delegates to the shared
/// ARCH-0096 <see cref="IdentifierComposer"/>; only the base-name anchor and the partition token rendering are
/// data-specific. A separate-container segmentation axis (ARCH-0101 §3) contributes a leading/trailing particle via
/// <see cref="StorageNameParticleRegistry"/> — folded here so the axis renders identically wherever a name is built.
/// </summary>
public static class StorageNameGenerator
{
    // Keyed by (provider, entity, partition, axis). The axis component is the signature of the ambient container-name
    // particles (e.g. "tenant=T1"): a separate-container axis makes the name ambient-dependent, so it MUST be in the
    // key — otherwise a per-axis container name (T1-Todo) would cache and serve across axis values (a container-level
    // cross-scope leak). Empty axis signature ⇒ byte-identical key and name to the pre-ARCH-0101 path.
    private static readonly ConcurrentDictionary<(string Provider, Type Entity, string? Partition, string Axis), string> Cache = new();

    // The base anchor (DATA-0104 grammar / NameOverride result) is stable per (provider, entity) and
    // INDEPENDENT of partition. Caching it here (ARCH-0096 §3, the deepest stable plane) means resolving a new
    // partition no longer re-runs the grammar — only the partition token + clamp are per-(provider,entity,partition).
    private static readonly ConcurrentDictionary<(string Provider, Type Entity), string> AnchorCache = new();

    /// <summary>Cached entry point used by the <see cref="INamingProvider"/> default implementation.</summary>
    public static string Resolve(string provider, Type entityType, string? partition, Func<StorageNamingCapability> capabilityFactory)
    {
        // Gather the ambient container-name particles (e.g. a separate-container tenant) BEFORE the cache lookup — they
        // determine the name AND must be in the cache key. Empty fast path when no axis is registered (IsEmpty).
        var extras = StorageNameParticleRegistry.Gather(entityType);
        var key = (provider, entityType, string.IsNullOrWhiteSpace(partition) ? null : partition.Trim(), AxisKey(extras));
        return Cache.GetOrAdd(key, static (k, ctx) =>
        {
            var cap = ctx.factory();
            var baseName = AnchorCache.GetOrAdd((k.Provider, k.Entity), static (ak, c) => ResolveAnchor(ak.Entity, c), cap);
            return Compose(baseName, k.Partition, cap, ctx.extras);
        }, (factory: capabilityFactory, extras));
    }

    /// <summary>Pure generation (no cache) — exposed for testing and direct use.</summary>
    public static string Generate(Type entityType, string? partition, StorageNamingCapability cap)
        => Compose(ResolveAnchor(entityType, cap), partition, cap, StorageNameParticleRegistry.Gather(entityType));

    // A deterministic signature of the ambient container-name particles for the cache key. "" when none ⇒ the name
    // is axis-independent and the key/name stay byte-identical to the pre-ARCH-0101 path. extras is pre-sorted.
    private static string AxisKey(Particle[] extras)
    {
        if (extras.Length == 0) return "";
        var sb = new StringBuilder();
        for (var i = 0; i < extras.Length; i++)
        {
            if (i > 0) sb.Append('|');
            sb.Append(extras[i].Axis).Append('=').Append(extras[i].Value ?? "");
        }
        return sb.ToString();
    }

    private static string ResolveAnchor(Type entityType, StorageNamingCapability cap)
        => cap.NameOverride?.Invoke(entityType) is { Length: > 0 } overridden
            ? overridden.Trim()
            : StorageNameResolver.Resolve(entityType, new StorageNameResolver.Convention(cap.Style, cap.Separator, cap.Casing));

    private static string Compose(string baseName, string? partition, StorageNamingCapability cap, Particle[] extras)
    {
        // Composition (ordering + position + separator + byte clamp) is the shared ARCH-0096 algorithm; the partition
        // token rendering stays adapter-specific (its PartitionTokenPolicy, applied to EVERY particle). When the adapter
        // isolates the partition through a native primitive (EncodePartitionInName = false, e.g. Couchbase scope) or
        // there is no partition, no partition particle is contributed; container-name particles (a separate-container
        // axis) still fold in around the anchor — "the axis is never in the spine".
        var policy = new CompositionPolicy(
            cap.PartitionSeparator.ToString(),
            new PartitionParticleFormatter(cap.Partition),
            cap.MaxIdentifierBytes);

        var trimmed = partition?.Trim();
        var hasPartition = cap.EncodePartitionInName && !string.IsNullOrEmpty(trimmed);

        // No axis particles ⇒ the exact pre-ARCH-0101 paths (alloc-free no-partition; single partition particle).
        if (extras.Length == 0)
        {
            if (!hasPartition)
                return IdentifierComposer.Compose(baseName, ReadOnlySpan<Particle>.Empty, policy);
            Span<Particle> only = [new Particle(0, "partition", trimmed)];
            return IdentifierComposer.Compose(baseName, only, policy);
        }

        // Fail-closed (ARCH-0101 §8): a container-name particle value MUST map injectively to its storage token under
        // this adapter's policy — else two distinct axis values collapse to ONE physical container (a cross-scope table
        // leak the raw-value cache key would hide). Policy-aware (rejects lossy chars AND, on a case-folding adapter,
        // mixed case), GUID-friendly. Runs only on a cache MISS, so valid axes pay it once. Mirrors the partition
        // front-door (PartitionNameValidator → the SAME PartitionTokenPolicy.IsInjective rule).
        for (var e = 0; e < extras.Length; e++)
        {
            if (!cap.Partition.IsInjective(extras[e].Value))
                throw new ArgumentException(
                    $"Container-name particle axis '{extras[e].Axis}' value '{extras[e].Value}' is not identifier-injective " +
                    "under this adapter's naming policy: a lossy / case-folded value could collapse two distinct scopes into " +
                    "one physical container. Use a GUID or an already-canonical token (letters/digits/-._, and lower-case on " +
                    "a case-folding store).");
        }

        // Axis particles present (e.g. a separate-container tenant): fold them with the partition particle and hand the
        // whole set to the ONE engine — it orders (by Order/axis), positions (leading/trailing), separates, and clamps.
        var particles = new Particle[extras.Length + (hasPartition ? 1 : 0)];
        var i = 0;
        if (hasPartition) particles[i++] = new Particle(0, "partition", trimmed);
        Array.Copy(extras, 0, particles, i, extras.Length);
        return IdentifierComposer.Compose(baseName, particles, policy);
    }
}
