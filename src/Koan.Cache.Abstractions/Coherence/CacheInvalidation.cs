using System;
using System.Collections.Generic;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Abstractions.Coherence;

/// <summary>
/// Transport-agnostic invalidation message broadcast across nodes.
/// </summary>
/// <remarks>
/// <para>
/// Sent by <c>LayeredCache</c> on entity writes and deletes; received by <c>CoherenceCoordinator</c>
/// on subscribing nodes. The receiver evicts L1 only — L2 is shared and was already evicted by
/// the writer.
/// </para>
/// <para>
/// <see cref="OriginNodeId"/> is the writer's per-process GUID. The coordinator drops messages
/// whose origin matches the local node id (origin filter) to avoid the writer evicting its own
/// just-written L1 entry.
/// </para>
/// </remarks>
public readonly record struct CacheInvalidation(
    CacheInvalidationKind Kind,
    CacheKey? Key,
    IReadOnlySet<string>? Tags,
    string? Region,
    string? ScopeId,
    Guid OriginNodeId,
    DateTimeOffset PublishedAtUtc)
{
    /// <summary>Build an <see cref="CacheInvalidationKind.EvictKey"/> message.</summary>
    public static CacheInvalidation EvictKey(CacheKey key, Guid originNodeId, string? region = null, string? scopeId = null)
        => new(CacheInvalidationKind.EvictKey, key, null, region, scopeId, originNodeId, DateTimeOffset.UtcNow);

    /// <summary>Build an <see cref="CacheInvalidationKind.EvictByTag"/> message.</summary>
    public static CacheInvalidation EvictByTag(IReadOnlySet<string> tags, Guid originNodeId, string? region = null, string? scopeId = null)
        => new(CacheInvalidationKind.EvictByTag, null, tags, region, scopeId, originNodeId, DateTimeOffset.UtcNow);

    /// <summary>Build an <see cref="CacheInvalidationKind.EvictAll"/> message.</summary>
    public static CacheInvalidation EvictAll(Guid originNodeId, string? region = null)
        => new(CacheInvalidationKind.EvictAll, null, null, region, null, originNodeId, DateTimeOffset.UtcNow);
}
