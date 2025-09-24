using Koan.Core.Hosting.Bootstrap;
using Koan.Orchestration.Models;

namespace Koan.Core.Adapters;

/// <summary>
/// Bootstrap report for adapter discovery and logging integration.
/// Provides structured data about adapter initialization status and capabilities.
/// </summary>
/// <param name="AdapterId">Unique adapter identifier</param>
/// <param name="DisplayName">Human-readable display name</param>
/// <param name="ServiceType">Service type category</param>
/// <param name="State">Bootstrap state (Success, Failed, Skipped)</param>
/// <param name="Message">Optional status message</param>
/// <param name="Duration">Optional initialization duration</param>
/// <param name="Metadata">Additional metadata</param>
public record BootstrapReport(
    string AdapterId,
    string DisplayName,
    ServiceType ServiceType,
    BootstrapState State,
    string? Message = null,
    TimeSpan? Duration = null,
    IReadOnlyDictionary<string, object>? Metadata = null)
{
    private readonly Dictionary<string, object> _metadata = new(Metadata ?? new Dictionary<string, object>());

    /// <summary>
    /// Create a successful bootstrap report
    /// </summary>
    public static BootstrapReport Success(string adapterId, string displayName, ServiceType serviceType, TimeSpan? duration = null, string? message = null)
        => new(adapterId, displayName, serviceType, BootstrapState.Success, message, duration);

    /// <summary>
    /// Create a failed bootstrap report
    /// </summary>
    public static BootstrapReport Failed(string adapterId, string displayName, ServiceType serviceType, string message, TimeSpan? duration = null)
        => new(adapterId, displayName, serviceType, BootstrapState.Failed, message, duration);

    /// <summary>
    /// Create a skipped bootstrap report
    /// </summary>
    public static BootstrapReport Skipped(string adapterId, string displayName, ServiceType serviceType, string message)
        => new(adapterId, displayName, serviceType, BootstrapState.Skipped, message);

    /// <summary>
    /// Add metadata to the bootstrap report
    /// </summary>
    public BootstrapReport WithMetadata(string key, object value)
    {
        _metadata[key] = value;
        return this with { Metadata = _metadata.AsReadOnly() };
    }

    /// <summary>
    /// Add multiple metadata entries to the bootstrap report
    /// </summary>
    public BootstrapReport WithMetadata(IReadOnlyDictionary<string, object> metadata)
    {
        foreach (var (key, value) in metadata)
        {
            _metadata[key] = value;
        }
        return this with { Metadata = _metadata.AsReadOnly() };
    }
}