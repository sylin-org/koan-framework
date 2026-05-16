namespace Koan.Cache.Abstractions.Coherence;

/// <summary>
/// Determines whether the <c>CoherenceCoordinator</c> activates at boot.
/// </summary>
public enum CoherenceMode
{
    /// <summary>
    /// Coordinator activates iff at least one <c>ICacheCoherenceChannel</c> is registered.
    /// Default — matches Reference = Intent: referencing a coherence-capable adapter just works.
    /// </summary>
    AutoDetect,

    /// <summary>
    /// Coordinator must activate. Boot fails fast if no channel is registered and a Remote tier
    /// is present — prevents silently-broken distributed deployments.
    /// </summary>
    Required,

    /// <summary>
    /// Coordinator is inactive even if channels are registered. Useful for single-node deployments
    /// that want to suppress broadcasts.
    /// </summary>
    Disabled
}
