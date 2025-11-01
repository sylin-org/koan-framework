namespace Koan.Services.Abstractions;

/// <summary>
/// Service descriptor built from [KoanService] attribute + configuration hierarchy.
/// Contains merged configuration from: attribute defaults → attribute explicit → appsettings → env vars.
/// </summary>
public class KoanServiceDescriptor
{
    /// <summary>
    /// Service identifier (e.g., "translation").
    /// </summary>
    public string ServiceId { get; set; } = "";

    /// <summary>
    /// Human-friendly display name.
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Service description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Service implementation type.
    /// </summary>
    public Type ServiceType { get; set; } = typeof(object);

    #region HTTP Endpoint (Tier 3)

    /// <summary>
    /// HTTP port for this service.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Health check endpoint path.
    /// </summary>
    public string HealthEndpoint { get; set; } = "/health";

    /// <summary>
    /// Service manifest endpoint path.
    /// </summary>
    public string ManifestEndpoint { get; set; } = "/.well-known/koan-service";

    #endregion

    #region Orchestrator Channel (Tier 1)

    /// <summary>
    /// Global orchestrator multicast group.
    /// </summary>
    public string OrchestratorMulticastGroup { get; set; } = "239.255.42.1";

    /// <summary>
    /// Global orchestrator multicast port.
    /// </summary>
    public int OrchestratorMulticastPort { get; set; } = 42001;

    /// <summary>
    /// Heartbeat interval.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; }

    /// <summary>
    /// Stale threshold.
    /// </summary>
    public TimeSpan StaleThreshold { get; set; }

    #endregion

    #region Service Channel (Tier 2, Optional)

    /// <summary>
    /// Enable service-specific channel.
    /// </summary>
    public bool EnableServiceChannel { get; set; }

    /// <summary>
    /// Service-specific multicast group.
    /// </summary>
    public string? ServiceMulticastGroup { get; set; }

    /// <summary>
    /// Service-specific multicast port.
    /// </summary>
    public int? ServiceMulticastPort { get; set; }

    #endregion

    /// <summary>
    /// Service capabilities (auto-detected or explicit).
    /// </summary>
    public string[] Capabilities { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Container image for Docker deployment.
    /// </summary>
    public string? ContainerImage { get; set; }

    /// <summary>
    /// Default container tag.
    /// </summary>
    public string? DefaultTag { get; set; }
}
