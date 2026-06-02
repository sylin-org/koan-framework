using System.Collections.Concurrent;

namespace Koan.Data.Abstractions.Naming;

/// <summary>
/// Single source of truth that turns an <c>(entityType, partition)</c> pair into a physical storage
/// identifier, honoring an adapter's announced <see cref="StorageNamingCapability"/>. Adapters announce
/// their capability; the framework owns the algorithm — so name generation can never drift between adapters.
/// </summary>
public static class StorageNameGenerator
{
    // Keyed by (provider, entity, partition). Capability is derived from options that are fixed after
    // startup, so caching on the provider identity is safe and mirrors the old per-factory caches.
    private static readonly ConcurrentDictionary<(string Provider, Type Entity, string? Partition), string> Cache = new();

    /// <summary>Cached entry point used by the <see cref="INamingProvider"/> default implementation.</summary>
    public static string Resolve(string provider, Type entityType, string? partition, Func<StorageNamingCapability> capabilityFactory)
    {
        var key = (provider, entityType, string.IsNullOrWhiteSpace(partition) ? null : partition.Trim());
        return Cache.GetOrAdd(key, k => Generate(k.Entity, k.Partition, capabilityFactory()));
    }

    /// <summary>Pure generation (no cache) — exposed for testing and direct use.</summary>
    public static string Generate(Type entityType, string? partition, StorageNamingCapability cap)
    {
        var baseName = cap.NameOverride?.Invoke(entityType) is { Length: > 0 } overridden
            ? overridden.Trim()
            : StorageNameResolver.Resolve(entityType, new StorageNameResolver.Convention(cap.Style, cap.Separator, cap.Casing));

        var trimmed = partition?.Trim();
        if (!cap.EncodePartitionInName || string.IsNullOrEmpty(trimmed))
            return Clamp(baseName, baseName, cap);

        var token = cap.Partition.Format(trimmed);
        var composed = baseName + cap.PartitionSeparator + token;
        return Clamp(composed, baseName, cap);
    }

    // When an identifier exceeds the adapter's byte limit, keep a readable prefix (the base name — which is
    // already short under HashedNamespace) and append a deterministic hash of the FULL identifier. Because
    // nothing in the framework ever reverse-parses a partition out of a name, replacing the overflowing tail
    // with a hash is safe: only uniqueness must be preserved, not recoverability. Names within the limit
    // (the common case) are returned unchanged.
    private static string Clamp(string identifier, string readableBase, StorageNamingCapability cap)
    {
        if (cap.MaxIdentifierBytes is not { } max || NamingUtils.ByteLength(identifier) <= max)
            return identifier;

        const int hashChars = 8;
        var hash = NamingUtils.ShortHash(identifier, hashChars);
        var prefix = NamingUtils.TrimToBytes(readableBase, max - hashChars - 1); // reserve sep + hash
        return prefix + cap.PartitionSeparator + hash;
    }
}
