namespace Koan.Data.Core.Options;

/// <summary>Host-owned persistence and admission limits for the active default Data route.</summary>
public sealed class DataRouteOptions
{
    /// <summary>
    /// Optional control-record path. Relative values are resolved beneath the host content root.
    /// The record contains identities only; it never contains connection strings or Entity values.
    /// </summary>
    public string? StatePath { get; set; }

    /// <summary>Maximum time a cutover waits for already-admitted mutations to finish.</summary>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(
        Infrastructure.Constants.Defaults.DataRouteDrainTimeoutSeconds);

    /// <summary>Maximum physical routes retained in the content-generation map.</summary>
    public int TrackedRoutes { get; set; } = Infrastructure.Constants.Defaults.DataRouteTrackedRoutes;
}
