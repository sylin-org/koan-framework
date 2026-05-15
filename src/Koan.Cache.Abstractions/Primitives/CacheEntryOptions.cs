using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Koan.Cache.Abstractions.Primitives;

/// <summary>
/// Developer-facing aggregate of all cache entry concerns (read, write, broadcast, metadata).
/// Used by the fluent builder and the public client surface. <c>LayeredCache</c> and
/// <c>ICacheStore</c> implementations consume the projected strict variants
/// <see cref="CacheReadOptions"/> and <see cref="CacheWriteOptions"/> via
/// <see cref="ToReadOptions"/> / <see cref="ToWriteOptions"/>.
/// </summary>
public sealed record CacheEntryOptions
{
    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>();

    /// <summary>Absolute time-to-live for the L2 (and default L1) entry.</summary>
    public TimeSpan? AbsoluteTtl { get; init; }

    /// <summary>L1-specific TTL override. Null = derive <c>max(30s, AbsoluteTtl/2)</c> at write time.</summary>
    public TimeSpan? L1AbsoluteTtl { get; init; }

    /// <summary>Sliding TTL — refreshed on each read when the store supports it.</summary>
    public TimeSpan? SlidingTtl { get; init; }

    /// <summary>How long to serve stale data while a background refresh runs.</summary>
    public TimeSpan? AllowStaleFor { get; init; }

    /// <summary>Per-call override for singleflight gate wait. Null = use <c>CacheOptions.DefaultSingleflightTimeout</c>.</summary>
    public TimeSpan? SingleflightTimeout { get; init; }

    /// <summary>Consistency guarantees expected from the store when serving reads.</summary>
    public CacheConsistencyMode Consistency { get; init; } = CacheConsistencyMode.StaleWhileRevalidate;

    /// <summary>Whether writes should broadcast a coherence invalidation. Default <c>true</c>.</summary>
    public bool ForceCoherenceBroadcast { get; init; } = true;

    /// <summary>Tags applied to entries created with these options. Used for bulk invalidation.</summary>
    public IReadOnlySet<string> Tags { get; init; } = EmptySet;

    /// <summary>Wire content type — guides serializer selection.</summary>
    public CacheContentKind ContentKind { get; init; } = CacheContentKind.Json;

    /// <summary>Optional region scope for tenant isolation.</summary>
    public string? Region { get; init; }

    /// <summary>Optional scope-id within a region for fine-grained partitioning.</summary>
    public string? ScopeId { get; init; }

    /// <summary>Arbitrary metadata attached to the entry. Available to instrumentation and diagnostics.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    /// <summary>Compute the absolute expiration timestamp relative to <paramref name="now"/>.</summary>
    public DateTimeOffset? CalculateAbsoluteExpiration(DateTimeOffset now)
        => AbsoluteTtl is { } ttl ? now.Add(ttl) : null;

    /// <summary>Return a copy with the given tags added (ordinal-ignore-case set semantics).</summary>
    public CacheEntryOptions WithTags(params string[] tags)
    {
        if (tags is null || tags.Length == 0) return this;

        var set = Tags is HashSet<string> existing
            ? new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(Tags, StringComparer.OrdinalIgnoreCase);

        foreach (var tag in tags)
        {
            if (!string.IsNullOrWhiteSpace(tag))
                set.Add(tag.Trim());
        }

        return this with { Tags = set };
    }

    /// <summary>Return a copy with the given metadata key/value set.</summary>
    public CacheEntryOptions WithMetadata(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key)) return this;

        var dict = Metadata is ConcurrentDictionary<string, string> concurrent
            ? new ConcurrentDictionary<string, string>(concurrent, StringComparer.Ordinal)
            : new ConcurrentDictionary<string, string>(Metadata, StringComparer.Ordinal);

        dict[key] = value;
        return this with { Metadata = dict };
    }

    /// <summary>Project the read-side options consumed by <c>LayeredCache.Read</c> and <c>ICacheStore.Fetch</c>.</summary>
    public CacheReadOptions ToReadOptions()
        => new(Region: Region, ScopeId: ScopeId, Consistency: Consistency, AllowStaleFor: AllowStaleFor);

    /// <summary>Project the write-side options consumed by <c>LayeredCache.Write</c> and <c>ICacheStore.Set</c>.</summary>
    public CacheWriteOptions ToWriteOptions()
        => new(
            AbsoluteTtl: AbsoluteTtl,
            L1AbsoluteTtl: L1AbsoluteTtl,
            SlidingTtl: SlidingTtl,
            AllowStaleFor: AllowStaleFor,
            Tags: Tags,
            Region: Region,
            ScopeId: ScopeId,
            ForceCoherenceBroadcast: ForceCoherenceBroadcast);

    /// <summary>
    /// Hydrate a <see cref="CacheEntryOptions"/> from a <see cref="CacheWriteOptions"/>.
    /// Fields not present on the write side keep their defaults (<see cref="ContentKind"/>,
    /// <see cref="SingleflightTimeout"/>, <see cref="Metadata"/>).
    /// </summary>
    public static CacheEntryOptions FromWriteOptions(CacheWriteOptions write)
        => new()
        {
            AbsoluteTtl = write.AbsoluteTtl,
            L1AbsoluteTtl = write.L1AbsoluteTtl,
            SlidingTtl = write.SlidingTtl,
            AllowStaleFor = write.AllowStaleFor,
            Tags = write.Tags,
            Region = write.Region,
            ScopeId = write.ScopeId,
            ForceCoherenceBroadcast = write.ForceCoherenceBroadcast,
        };
}
