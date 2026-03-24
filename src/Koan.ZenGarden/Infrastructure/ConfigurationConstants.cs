namespace Koan.ZenGarden.Infrastructure;

internal static class ConfigurationConstants
{
    public const string Section = "Koan:ZenGarden";

    public static class Keys
    {
        public const string Endpoint = nameof(Endpoint);
        public const string EnableDiscovery = nameof(EnableDiscovery);
        public const string DiscoveryTimeoutSeconds = nameof(DiscoveryTimeoutSeconds);
        public const string DiscoveryPort = nameof(DiscoveryPort);
        public const string DiscoveryMulticastGroup = nameof(DiscoveryMulticastGroup);
        public const string DiscoveryCacheTtlSeconds = nameof(DiscoveryCacheTtlSeconds);
        public const string DiscoveryEnableBroadcastFallback = nameof(DiscoveryEnableBroadcastFallback);
        public const string DiscoveryEnableLimitedBroadcast = nameof(DiscoveryEnableLimitedBroadcast);
        public const string HttpTimeoutSeconds = nameof(HttpTimeoutSeconds);
        public const string StreamReconnectDelaySeconds = nameof(StreamReconnectDelaySeconds);
        public const string DedupeWindowSize = nameof(DedupeWindowSize);
        public const string RequireHostMossWhenContainerized = nameof(RequireHostMossWhenContainerized);
        public const string ContainerHost = nameof(ContainerHost);
        public const string ContainerHostPort = nameof(ContainerHostPort);
        public const string PersistDiscoveryCache = nameof(PersistDiscoveryCache);
        public const string DiscoveryCachePath = nameof(DiscoveryCachePath);
        public const string PersistedCacheTtlHours = nameof(PersistedCacheTtlHours);
        public const string PreferredStoneName = nameof(PreferredStoneName);
        public const string KoiDiscoveryEnabled = nameof(KoiDiscoveryEnabled);
        public const string KoiEndpoint = nameof(KoiEndpoint);
        public const string KoiHealthTimeout = nameof(KoiHealthTimeout);
        public const string KoiContinuousDiscovery = nameof(KoiContinuousDiscovery);
        public const string KoiLanternDiscovery = nameof(KoiLanternDiscovery);
        public const string KoiRetryInterval = nameof(KoiRetryInterval);
    }

    /// <summary>
    /// Builds full configuration path: "Koan:ZenGarden:{key}".
    /// </summary>
    public static string FullKey(string key) => $"{Section}:{key}";
}
