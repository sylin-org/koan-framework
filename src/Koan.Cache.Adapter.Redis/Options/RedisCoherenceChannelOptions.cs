using System;

namespace Koan.Cache.Adapter.Redis.Options;

/// <summary>
/// Options for the Redis pub/sub coherence channel. Storage-side options live on
/// <see cref="RedisCacheAdapterOptions"/>; pub/sub-specific knobs live here.
/// </summary>
public sealed class RedisCoherenceChannelOptions
{
    /// <summary>Pub/sub channel name. Same value must be used across all nodes in the cluster.</summary>
    public string ChannelName { get; set; } = "koan-cache";

    /// <summary>Maximum time to wait for a publish round-trip before logging a warning.</summary>
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(2);
}
