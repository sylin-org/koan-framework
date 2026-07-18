using System;

namespace Koan.Cache.Abstractions.Policies;

/// <summary>
/// Canonical L1 TTL derivation rule. Single source of truth consumed by both the
/// boot-time materializer (<c>CachePolicyMaterializer.ResolveL1Ttl</c>) and the per-write
/// path (<c>CacheWriteOptions.GetEffectiveL1Ttl</c>) so the invariant
/// <c>L1Ttl ≤ AbsoluteTtl</c> can't drift between call sites.
/// </summary>
/// <remarks>
/// <para>
/// History: prior to consolidation, the derivation was implemented independently in two
/// places. The boot-time path correctly clamped to L2; the per-write path did not. Result:
/// for any L2 TTL under 60 seconds, the per-write derivation produced an L1 TTL longer
/// than L2 so local staleness remains bounded independently of shared storage.
/// Extracting the rule eliminates the bug class (one implementation, no drift).
/// </para>
/// <para>
/// The rule itself (ARCH-0075 invariant): when no explicit L1 override is given,
/// <c>L1Ttl = min(AbsoluteTtl, max(30s, AbsoluteTtl/2))</c>. The 30s floor is
/// defense-in-depth: long L2 TTLs still get a refresh chance reasonably often even if
/// cross-node coherence is silent. The min-clamp prevents the floor from exceeding L2.
/// </para>
/// </remarks>
public static class CacheL1TtlPolicy
{
    /// <summary>
    /// Defense-in-depth floor for derived L1 TTL. Caps worst-case staleness when no
    /// explicit override is supplied and coherence broadcasts are missed.
    /// </summary>
    public static readonly TimeSpan DefaultFloor = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Derive the effective L1 TTL. Explicit <paramref name="l1Override"/> wins. When unset
    /// AND <paramref name="absoluteTtl"/> has a value, returns
    /// <c>min(absoluteTtl, max(<see cref="DefaultFloor"/>, absoluteTtl/2))</c>. Returns null
    /// when both are null (no expiration).
    /// </summary>
    public static TimeSpan? Derive(TimeSpan? absoluteTtl, TimeSpan? l1Override)
        => Derive(absoluteTtl, l1Override, DefaultFloor);

    /// <summary>
    /// Internal-test overload allowing a custom floor. Not for production code.
    /// </summary>
    public static TimeSpan? Derive(TimeSpan? absoluteTtl, TimeSpan? l1Override, TimeSpan floor)
    {
        if (l1Override.HasValue) return l1Override;
        if (!absoluteTtl.HasValue) return null;

        var absSeconds = absoluteTtl.Value.TotalSeconds;
        var derived = Math.Max(floor.TotalSeconds, absSeconds / 2.0);
        var clamped = Math.Min(absSeconds, derived);
        return TimeSpan.FromSeconds(clamped);
    }
}
