namespace Koan.Services.Abstractions;

/// <summary>
/// Declares a class as a Koan service with attribute-driven configuration.
/// Follows the same declarative pattern as [DataAdapter] and [VectorAdapter].
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class KoanServiceAttribute : Attribute
{
    /// <summary>
    /// Unique service identifier (e.g., "translation", "ocr", "speech-to-text").
    /// Required. Used for discovery and routing.
    /// </summary>
    public string ServiceId { get; set; }

    /// <summary>
    /// Human-friendly display name.
    /// Default: ServiceId converted to title case with spaces.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Service description for documentation and manifests.
    /// </summary>
    public string? Description { get; set; }

    #region HTTP Endpoint Configuration (Tier 3 - Request/Response Data Plane)

    /// <summary>
    /// HTTP port for this service instance.
    /// Default: 8080
    /// Overridable via: appsettings.json "Koan:Services:{ServiceId}:Port"
    /// </summary>
    public int Port { get; set; } = 8080;

    /// <summary>
    /// Health check endpoint path.
    /// Default: "/health"
    /// </summary>
    public string HealthEndpoint { get; set; } = "/health";

    /// <summary>
    /// Service manifest endpoint path (RFC 8615 .well-known).
    /// Default: "/.well-known/koan-service"
    /// </summary>
    public string ManifestEndpoint { get; set; } = "/.well-known/koan-service";

    #endregion

    #region Orchestrator Channel Configuration (Tier 1 - Discovery & Control Plane)

    /// <summary>
    /// Global orchestrator multicast group. ALL services join this channel.
    /// Default: "239.255.42.1"
    /// Rarely overridden (global configuration).
    /// Overridable via: appsettings.json "Koan:Services:Orchestrator:MulticastGroup"
    /// </summary>
    public string OrchestratorMulticastGroup { get; set; } = "239.255.42.1";

    /// <summary>
    /// Global orchestrator multicast port.
    /// Default: 42001
    /// Rarely overridden (global configuration).
    /// Overridable via: appsettings.json "Koan:Services:Orchestrator:MulticastPort"
    /// </summary>
    public int OrchestratorMulticastPort { get; set; } = 42001;

    /// <summary>
    /// Heartbeat interval in seconds.
    /// Service announces itself to orchestrator channel every N seconds.
    /// Default: 30 seconds
    /// </summary>
    public int HeartbeatIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Stale threshold in seconds.
    /// Service instances removed if no heartbeat for N seconds.
    /// Default: 120 seconds (2 minutes)
    /// </summary>
    public int StaleThresholdSeconds { get; set; } = 120;

    #endregion

    #region Service-Specific Channel Configuration (Tier 2 - Service Dialog Plane, Optional)

    /// <summary>
    /// Enable service-specific multicast channel for pub/sub within service boundaries.
    /// Default: false (most services don't need this)
    /// When true, all instances of this service join the same channel for coordination.
    /// Use cases: cache invalidation, config updates, coordinated actions.
    /// </summary>
    public bool EnableServiceChannel { get; set; } = false;

    /// <summary>
    /// Service-specific multicast group (e.g., "239.255.42.10" for translation).
    /// Only used if EnableServiceChannel = true.
    /// Null = auto-generate based on service ID.
    /// </summary>
    public string? ServiceMulticastGroup { get; set; } = null;

    /// <summary>
    /// Service-specific multicast port (e.g., 42010 for translation).
    /// Only used if EnableServiceChannel = true.
    /// Null = auto-generate based on service ID.
    /// </summary>
    public int? ServiceMulticastPort { get; set; } = null;

    #endregion

    #region Capability Detection

    /// <summary>
    /// Service capabilities (e.g., ["translate", "detect-language"]).
    /// Null = auto-detect from public Task&lt;T&gt; methods.
    /// Method names converted to kebab-case (DetectLanguage → "detect-language").
    /// </summary>
    public string[]? Capabilities { get; set; } = null;

    #endregion

    #region Deployment Hints

    /// <summary>
    /// Container image for Docker deployment (e.g., "koan/service-translation").
    /// Optional. Used by orchestration tooling.
    /// </summary>
    public string? ContainerImage { get; set; } = null;

    /// <summary>
    /// Default container tag (e.g., "latest", "1.0.0").
    /// Default: "latest"
    /// </summary>
    public string? DefaultTag { get; set; } = "latest";

    #endregion

    public KoanServiceAttribute(string serviceId)
    {
        ServiceId = serviceId ?? throw new ArgumentNullException(nameof(serviceId));
    }
}
