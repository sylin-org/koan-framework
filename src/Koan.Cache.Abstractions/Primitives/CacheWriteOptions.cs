using System;
using System.Collections.Generic;

namespace Koan.Cache.Abstractions.Primitives;

/// <summary>
/// Options that govern a single cache write. Carries TTL, tags, scope, and the
/// coherence-broadcast flag.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="L1AbsoluteTtl"/> is the L1-specific TTL override. When null, the cache pillar
/// derives a defense-in-depth default of <c>max(30s, AbsoluteTtl / 2)</c> at write time —
/// this caps worst-case L1 staleness when coherence is silent.
/// </para>
/// <para>
/// <see cref="ForceCoherenceBroadcast"/> controls whether the layered cache publishes an
/// invalidation on this write. Default true; set false for cache-populating reads where no
/// data change occurred.
/// </para>
/// </remarks>
public sealed record CacheWriteOptions(
    TimeSpan? AbsoluteTtl,
    TimeSpan? L1AbsoluteTtl,
    TimeSpan? SlidingTtl,
    TimeSpan? AllowStaleFor,
    IReadOnlySet<string> Tags,
    string? Region,
    string? ScopeId,
    bool ForceCoherenceBroadcast)
{
    private static readonly IReadOnlySet<string> EmptyTags = new HashSet<string>();

    /// <summary>Default write options: no expiration, no tags, broadcast enabled.</summary>
    public static CacheWriteOptions Default { get; } = new(
        AbsoluteTtl: null,
        L1AbsoluteTtl: null,
        SlidingTtl: null,
        AllowStaleFor: null,
        Tags: EmptyTags,
        Region: null,
        ScopeId: null,
        ForceCoherenceBroadcast: true);

    /// <summary>
    /// Compute the effective L1 TTL for this write. When <see cref="L1AbsoluteTtl"/> is set, returns
    /// that value verbatim. Otherwise derives <c>min(AbsoluteTtl, max(30s, AbsoluteTtl / 2))</c>
    /// for defense in depth: clamped to <see cref="AbsoluteTtl"/> so L1 never outlives L2.
    /// Returns null only when <see cref="AbsoluteTtl"/> is also null (no expiration).
    /// </summary>
    public TimeSpan? GetEffectiveL1Ttl()
    {
        if (L1AbsoluteTtl.HasValue) return L1AbsoluteTtl;
        if (!AbsoluteTtl.HasValue) return null;

        var absSeconds = AbsoluteTtl.Value.TotalSeconds;
        var half = absSeconds / 2.0;
        var floor = 30.0;
        // Inner: max(30s, half) — defense-in-depth ceiling.
        // Outer: min(absoluteTtl, inner) — L1 must never outlive L2.
        var derived = Math.Min(absSeconds, Math.Max(floor, half));
        return TimeSpan.FromSeconds(derived);
    }
}
