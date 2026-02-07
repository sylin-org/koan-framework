namespace Koan.ZenGarden;

/// <summary>
/// Configuration options for Zen Garden tools-domain integration.
/// </summary>
public sealed class ZenGardenOptions
{
    public const string SectionName = "Koan:ZenGarden";

    /// <summary>
    /// Optional explicit Moss endpoint (for example "http://stone-01:7185").
    /// When not set, endpoint is resolved via discovery.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Enable Moss discovery and automatic rebind when connection fails.
    /// </summary>
    public bool EnableDiscovery { get; set; } = true;

    /// <summary>
    /// Discovery request timeout in seconds.
    /// </summary>
    public int DiscoveryTimeoutSeconds { get; set; } = Constants.Discovery.DefaultTimeoutSeconds;

    /// <summary>
    /// Discovery UDP port (default 7184).
    /// </summary>
    public int DiscoveryPort { get; set; } = Constants.Discovery.DefaultPort;

    /// <summary>
    /// Discovery multicast group (default 239.255.42.99).
    /// </summary>
    public string DiscoveryMulticastGroup { get; set; } = Constants.Discovery.DefaultMulticastGroup;

    /// <summary>
    /// Cache TTL for discovered stones.
    /// </summary>
    public int DiscoveryCacheTtlSeconds { get; set; } = Constants.Discovery.DefaultCacheTtlSeconds;

    /// <summary>
    /// Enable directed broadcast fallback discovery.
    /// </summary>
    public bool DiscoveryEnableBroadcastFallback { get; set; } = true;

    /// <summary>
    /// Enable limited broadcast fallback (255.255.255.255).
    /// </summary>
    public bool DiscoveryEnableLimitedBroadcast { get; set; } = false;

    /// <summary>
    /// HTTP timeout used for snapshot and stream requests.
    /// </summary>
    public int HttpTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Delay between stream reconnect attempts after failures.
    /// </summary>
    public int StreamReconnectDelaySeconds { get; set; } = 3;

    /// <summary>
    /// Max number of event ids kept for dedupe.
    /// </summary>
    public int DedupeWindowSize { get; set; } = 4096;

    /// <summary>
    /// When true and runtime is containerized, require Moss to be reachable on the container host
    /// instead of relying on UDP discovery.
    /// </summary>
    public bool RequireHostMossWhenContainerized { get; set; } = true;

    /// <summary>
    /// Host alias or explicit endpoint used when containerized.
    /// Examples: "host.docker.internal", "moss-host", "http://moss-host:7185".
    /// </summary>
    public string ContainerHost { get; set; } = "host.docker.internal";

    /// <summary>
    /// Moss port used when ContainerHost is a hostname without explicit port.
    /// </summary>
    public int ContainerHostPort { get; set; } = Constants.Moss.DefaultPort;
}
