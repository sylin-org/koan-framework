using System;
using System.Collections.Generic;
using System.Reflection;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Abstractions.Policies;

/// <summary>
/// Runtime, immutable materialization of <see cref="CachePolicyAttribute"/> + ambient context.
/// Built by <c>CachePolicyMaterializer</c> at startup; consumed by <c>CachedRepository</c> and
/// <c>LayeredCache</c>.
/// </summary>
public sealed record CachePolicyDescriptor(
    CacheScope Scope,
    string KeyTemplate,
    CacheStrategy Strategy,
    CacheConsistencyMode Consistency,
    CacheTier Tier,
    TimeSpan? AbsoluteTtl,
    TimeSpan? L1AbsoluteTtl,
    TimeSpan? SlidingTtl,
    TimeSpan? AllowStaleFor,
    IReadOnlyList<string> Tags,
    string? Region,
    string? ScopeId,
    string? LocalProvider,
    string? RemoteProvider,
    bool ForceCoherenceBroadcast,
    IReadOnlyDictionary<string, string> Metadata,
    MemberInfo? TargetMember,
    Type? DeclaringType)
{
    /// <summary>
    /// Project the developer-facing aggregate options. <see cref="ToReadOptions"/> and
    /// <see cref="ToWriteOptions"/> are the strict tier-side variants consumed by
    /// <c>LayeredCache</c> and <c>ICacheStore</c>.
    /// </summary>
    public CacheEntryOptions ToOptions()
        => new()
        {
            AbsoluteTtl = AbsoluteTtl,
            L1AbsoluteTtl = L1AbsoluteTtl,
            SlidingTtl = SlidingTtl,
            AllowStaleFor = AllowStaleFor,
            Consistency = Consistency,
            ForceCoherenceBroadcast = ForceCoherenceBroadcast,
            Tags = new HashSet<string>(Tags, StringComparer.OrdinalIgnoreCase),
            Region = Region,
            ScopeId = ScopeId,
            Metadata = new Dictionary<string, string>(Metadata, StringComparer.Ordinal),
        };

    /// <summary>Project the read-side options carried by this policy.</summary>
    public CacheReadOptions ToReadOptions()
        => new(
            Region: Region,
            ScopeId: ScopeId,
            Consistency: Consistency,
            AllowStaleFor: AllowStaleFor);

    /// <summary>Project the write-side options carried by this policy.</summary>
    public CacheWriteOptions ToWriteOptions()
        => new(
            AbsoluteTtl: AbsoluteTtl,
            L1AbsoluteTtl: L1AbsoluteTtl,
            SlidingTtl: SlidingTtl,
            AllowStaleFor: AllowStaleFor,
            Tags: new HashSet<string>(Tags, StringComparer.OrdinalIgnoreCase),
            Region: Region,
            ScopeId: ScopeId,
            ForceCoherenceBroadcast: ForceCoherenceBroadcast);
}
