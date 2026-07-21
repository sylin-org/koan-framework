using Koan.Cache.Abstractions;

namespace Koan.Cache.Adapter.Redis.Infrastructure;

internal static class Constants
{
    internal const string ProviderId = "redis";
    internal const string BroadcastProviderId = "redis-cache";
    internal const string ReferenceIdentity = "Koan.Cache.Adapter.Redis";
    internal const int ProviderPriority = 100;
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
