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
    /// Compute the effective L1 TTL for this write. Delegates to
    /// <see cref="Policies.CacheL1TtlPolicy.Derive(TimeSpan?, TimeSpan?)"/> — the single
    /// source of truth shared with the boot-time materializer so the rule can't drift
    /// between call sites.
    /// </summary>
    public TimeSpan? GetEffectiveL1Ttl()
        => Policies.CacheL1TtlPolicy.Derive(AbsoluteTtl, L1AbsoluteTtl);
}
