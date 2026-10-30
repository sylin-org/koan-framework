using Koan.Cache.Abstractions;

namespace Koan.Cache.Adapter.Redis.Infrastructure;

internal static class Constants
{
    internal const string ProviderId = "redis";
    internal const string BroadcastProviderId = "redis-cache";
    internal const string ReferenceIdentity = "Koan.Cache.Adapter.Redis";
    internal const int ProviderPriority = 100;

    /// <summary>
    /// Peer invalidations waiting for this node's broadcast pump. They are small and lossy by design — L1 TTL bounds
    /// staleness — so a full inbox drops the newest one.
    /// </summary>
    internal const int BroadcastIngressCapacity = 1024;
    internal const string DefaultKeyPrefix = "cache:";
    internal const string DefaultTagPrefix = "cache:tag:";
    internal const string DefaultChannelName = "koan-cache";

    internal static class Configuration
    {
        internal const string Section = CacheConstants.Configuration.Redis.Section;
        internal const string ChannelName = CacheConstants.Configuration.Redis.ChannelName;
        internal const string KeyPrefix = CacheConstants.Configuration.Redis.KeyPrefix;
    }
}
