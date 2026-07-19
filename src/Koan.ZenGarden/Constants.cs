namespace Koan.ZenGarden;

/// <summary>
/// Constants for Zen Garden tools-domain integration.
/// </summary>
internal static class Constants
{
    internal static class Composition
    {
        public const string SourceId = "zengarden-offering";
        public const string IntentScheme = "zen-garden";
    }

    public static class Discovery
    {
        public const int DefaultTimeoutSeconds = 3;
        public const int DefaultPort = 7184;
        public const string DefaultMulticastGroup = "239.255.42.99";
        public const int DefaultCacheTtlSeconds = 90;
        public const string RequestType = "discovery_request";
        public const string ResponseType = "discovery_response";
        public const string DiscoverTargetMoss = "moss";
    }

    public static class EnvironmentVariables
    {
        public const string GardenStone = "GARDEN_STONE";
        public const string DotnetRunningInContainer = "DOTNET_RUNNING_IN_CONTAINER";
        public const string GardenDiscoveryTimeoutSeconds = "GARDEN_DISCOVERY_TIMEOUT_SECS";
        public const string DiscoveryPort = "DISCOVERY_PORT";
        public const string DiscoveryMulticastGroup = "DISCOVERY_MCAST_GROUP";
        public const string DiscoveryEnableBroadcastFallback = "DISCOVERY_ENABLE_BCAST_FALLBACK";
        public const string DiscoveryEnableLimitedBroadcast = "DISCOVERY_ENABLE_LIMITED_BCAST";
        public const string ContainerHost = "KOAN_ZENGARDEN_CONTAINER_HOST";
        public const string ContainerHostPort = "KOAN_ZENGARDEN_CONTAINER_HOST_PORT";
        public const string RequireHostMossWhenContainerized = "KOAN_ZENGARDEN_REQUIRE_HOST_MOSS";
        public const string CachePath = "KOAN_ZENGARDEN_CACHE_PATH";
        public const string PreferredStoneName = "KOAN_ZENGARDEN_PREFERRED_STONE";
        public const string KoiEndpoint = "KOAN_ZENGARDEN_KOI_ENDPOINT";
        public const string KoiEnabled = "KOAN_ZENGARDEN_KOI_ENABLED";
    }

    public static class Persistence
    {
        public const int DefaultPersistedCacheTtlHours = 168; // 7 days
        public const string DefaultCacheSubdirectory = ".Koan/zen-garden";
        public const string RosterFileName = "garden-stones.json";
        public const string LegacyRosterFileName = "stones.json";
        public const string MossTopologyFileName = "garden-topology.json";
    }

    public static class Koi
    {
        public const int DefaultPort = 5641;
        public const string HealthEndpoint = "/healthz";
        public const string StatusEndpoint = "/v1/status";
        public const string BrowseEndpoint = "/v1/mdns/discover";
        public const string EventsEndpoint = "/v1/mdns/subscribe";
        public const string MossServiceType = "_moss._tcp";
        public const string LanternServiceType = "_lantern._tcp";
    }

    public static class Moss
    {
        public const int DefaultPort = 7185;
        public const string HealthEndpoint = "/health";
        public const string TopologyEndpoint = "/api/v1/garden/topology";
        public const string ToolsEndpoint = "/api/v1/garden/tools";
        public const string ToolsStreamEndpoint = "/api/v1/garden/tools/stream";
        public const string CapabilityEnsureEndpointFormat = "/api/v1/stone/offerings/{0}/capabilities";
        public const int TopologyHydrationIntervalMinutes = 5;
    }
}
