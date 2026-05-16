namespace Koan.Cache.Abstractions.Coherence;

/// <summary>
/// Declares what a coherence channel can deliver. Distinct from store capabilities —
/// these describe broadcast semantics, not K/V storage.
/// </summary>
public sealed record CoherenceCapabilities(
    bool SupportsCatchUp,
    bool GuaranteesAtLeastOnce,
    bool PreservesPerKeyOrder)
{
    /// <summary>Pure best-effort pub/sub: no catch-up, at-most-once, no order guarantees.</summary>
    public static CoherenceCapabilities BestEffort { get; } = new(
        SupportsCatchUp: false,
        GuaranteesAtLeastOnce: false,
        PreservesPerKeyOrder: false);
}
