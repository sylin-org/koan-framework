namespace Koan.Storage.Replication;

/// <summary>
/// Represents a known storage object across both cache and durable tiers.
/// Immutable record — create new instances for state transitions.
/// </summary>
public sealed record ManifestEntry
{
    /// <summary>Object key (path within container).</summary>
    public required string Key { get; init; }

    /// <summary>Object size in bytes.</summary>
    public long Size { get; init; }

    /// <summary>Entity tag for conflict detection. Null when unknown.</summary>
    public string? ETag { get; init; }

    /// <summary>True if the object is present in the local cache provider.</summary>
    public bool Cached { get; init; }

    /// <summary>True if the object has been confirmed present on the durable provider.</summary>
    public bool Synced { get; init; }

    /// <summary>Last time this object was accessed (for LRU eviction ordering).</summary>
    public DateTimeOffset LastAccess { get; init; }
}
