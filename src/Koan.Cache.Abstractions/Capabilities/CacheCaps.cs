using Koan.Core.Capabilities;

namespace Koan.Cache.Abstractions.Capabilities;

/// <summary>Provider guarantees understood by the Cache pillar.</summary>
public static class CacheCaps
{
    public static readonly Capability Tags = new("cache.tags");
    public static readonly Capability SlidingExpiration = new("cache.expiration.sliding");
    public static readonly Capability BoundedStaleServing = new("cache.read.stale-bounded");
    public static readonly Capability BinaryPayload = new("cache.payload.binary");
    public static readonly Capability Persistent = new("cache.storage.persistent");

    public static CapabilitySet Describe(object source, string? owner = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CapabilityResolver.TryDescribe(source, owner) ?? new CapabilitySet(owner);
    }
}
