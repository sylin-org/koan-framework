namespace Koan.Cache.Abstractions.Coherence;

/// <summary>
/// Determines whether the <c>CoherenceCoordinator</c> activates at boot.
/// </summary>
public enum CoherenceMode
{
    /// <summary>
    /// Peer invalidation activates for a layered topology. Communication supplies a process-local floor and
    /// automatically elects a stronger node-broadcast provider when one is active.
    /// </summary>
    AutoDetect,

    /// <summary>
    /// A layered topology requires a non-local node-broadcast provider. Boot fails rather than silently
    /// accepting process-local reach.
    /// </summary>
    Required,

    /// <summary>
    /// Peer invalidation is inactive even when a node-broadcast provider is available.
    /// </summary>
    Disabled
}
