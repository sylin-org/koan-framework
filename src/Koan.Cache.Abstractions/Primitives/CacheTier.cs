namespace Koan.Cache.Abstractions.Primitives;

/// <summary>
/// Controls which cache tiers are used for an entity or policy.
/// Default is Layered — reads L1 then L2, writes to both.
/// Degrades gracefully: if no remote provider, Layered behaves as LocalOnly.
/// </summary>
public enum CacheTier
{
    /// <summary>L1 (local) + L2 (remote) with automatic fallback. Default.</summary>
    Layered,

    /// <summary>L1 only — skip remote even if available.</summary>
    LocalOnly,

    /// <summary>L2 only — skip local cache (for large objects or shared-only state).</summary>
    RemoteOnly
}
