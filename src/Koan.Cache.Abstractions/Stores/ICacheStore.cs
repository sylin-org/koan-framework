using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Abstractions.Stores;

/// <summary>
/// Pure K/V cache store contract. Distributed coherence is NOT a store concern —
/// see <c>ICacheCoherenceChannel</c> in <c>Koan.Cache.Abstractions.Coherence</c>.
/// </summary>
/// <remarks>
/// <para>
/// Implementations declare <see cref="Placement"/> (Local or Remote) and capability flags so the
/// topology resolver can assign them to L1 vs L2 tiers. <c>[ProviderPriority(N)]</c> on the
/// implementation class controls precedence when multiple stores share a placement.
/// </para>
/// </remarks>
public interface ICacheStore
{
    /// <summary>Unique store identifier, matched against <c>CacheOptions.LocalProvider</c> / <c>RemoteProvider</c>.</summary>
    string Name { get; }

    /// <summary>Whether this store is process-local or shared across nodes.</summary>
    CacheStorePlacement Placement { get; }

    /// <summary>Declared K/V capabilities (tags, sliding TTL, SWR, binary, persistence).</summary>
    CacheStoreCapabilities Capabilities { get; }

    /// <summary>Fetch a single entry. Returns a miss result rather than throwing on absence.</summary>
    ValueTask<CacheFetchResult> Fetch(CacheKey key, CacheReadOptions options, CancellationToken ct);

    /// <summary>Set a single entry with the given write options (TTL, tags, region).</summary>
    ValueTask Set(CacheKey key, CacheValue value, CacheWriteOptions options, CancellationToken ct);

    /// <summary>Remove a single entry. Returns true iff the entry existed prior.</summary>
    ValueTask<bool> Remove(CacheKey key, CancellationToken ct);

    /// <summary>Check existence without fetching the payload.</summary>
    ValueTask<bool> Exists(CacheKey key, CancellationToken ct);

    /// <summary>Update an entry's absolute TTL without touching its value. No-op if the entry doesn't exist.</summary>
    ValueTask Touch(CacheKey key, TimeSpan? newAbsoluteTtl, CancellationToken ct);

    /// <summary>Stream every key currently associated with <paramref name="tag"/>. Used by tag-flush operations.</summary>
    IAsyncEnumerable<TaggedCacheKey> EnumerateByTag(string tag, CancellationToken ct);
}
