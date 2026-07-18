using Koan.Cache.Adapter.Redis.Infrastructure;

namespace Koan.Cache.Adapter.Redis.Options;

/// <summary>Cache-specific Redis pub/sub settings for the every-node framework-broadcast capability.</summary>
public sealed class RedisCacheBroadcastOptions
{
    /// <summary>Channel prefix shared by nodes in the same Cache mesh.</summary>
    public string ChannelName { get; set; } = Constants.DefaultChannelName;

    /// <summary>Maximum time to wait for Redis to accept one peer invalidation.</summary>
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(2);
}
