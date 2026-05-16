using Koan.ZenGarden.Infrastructure;

namespace Koan.ZenGarden;

/// <summary>
/// Configuration options for Zen Garden tools-domain integration.
/// </summary>
public sealed class ZenGardenOptions
{
    public const string SectionName = ConfigurationConstants.Section;

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

    /// <summary>
    /// When true, discovered Stone topology is persisted to disk for failover recovery.
    /// </summary>
    public bool PersistDiscoveryCache { get; set; } = true;

    /// <summary>
    /// Explicit path to the Stone roster cache directory.
    /// When null, auto-resolved from environment or convention (.Koan/zen-garden/).
    /// </summary>
    public string? DiscoveryCachePath { get; set; }

    /// <summary>
    /// TTL in hours for persisted Stone entries (default 168 = 7 days).
    /// Persisted entries use a longer TTL than in-memory entries to support
    /// failover scenarios where the primary Moss may be down for extended periods.
    /// </summary>
    public int PersistedCacheTtlHours { get; set; } = Constants.Persistence.DefaultPersistedCacheTtlHours;

    /// <summary>
    /// Preferred Moss Stone name for soft-affinity binding.
    /// When set, the adapter tries to connect to the named Stone before falling
    /// back to the container host or general discovery. Supports stone names,
    /// stone IDs, host:port, and .local suffixes.
    /// </summary>
    public string? PreferredStoneName { get; set; }

    // ── Orchestrator proxy (model recommendations) ───────────────────

    /// <summary>
    /// Explicit orchestrator proxy endpoint (e.g. "http://localhost:21434").
    /// When null, resolved via ZenGarden offering catalog (ollama::orchestrator).
    /// </summary>
    public string? OrchestratorProxyEndpoint { get; set; }

    /// <summary>
    /// TTL in seconds for cached model recommendations. Default: 300 (5 minutes).
    /// </summary>
    public int RecommendationCacheTtlSeconds { get; set; } = 300;

    // ── Koi topology handler ─────────────────────────────────────────

    /// <summary>
    /// Explicit Koi daemon endpoint (e.g. "http://localhost:5641").
    /// When null, auto-detected from container state or defaults to localhost.
    /// </summary>
    public string? KoiEndpoint { get; set; }

    /// <summary>
    /// Enable the background Koi topology handler.
    /// When true, the handler probes for Koi at startup and maintains a live topology projection.
    /// </summary>
    public bool KoiDiscoveryEnabled { get; set; } = true;

    /// <summary>
    /// Timeout for the Koi health probe. Keep short — this gates the fast-fail path.
    /// </summary>
    public TimeSpan KoiHealthTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Idle timeout for Koi browse requests. The SSE stream closes after this duration
    /// of silence, signaling that all currently known services have been reported.
    /// </summary>
    public TimeSpan KoiBrowseIdleTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Enable continuous SSE event streaming from Koi for real-time topology updates.
    /// When false, the handler performs a single browse and does not maintain a stream.
    /// </summary>
    public bool KoiContinuousDiscovery { get; set; } = true;

    /// <summary>
    /// Also browse for <c>_lantern._tcp</c> services via Koi for cross-subnet topology.
    /// </summary>
    public bool KoiLanternDiscovery { get; set; } = true;

    /// <summary>
    /// Interval between Koi re-probe attempts when in <c>NotDetected</c> state,
    /// and the maximum backoff cap when in <c>Reconnecting</c> state.
    /// </summary>
    public TimeSpan KoiRetryInterval { get; set; } = TimeSpan.FromSeconds(30);
}
