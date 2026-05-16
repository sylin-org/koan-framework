using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Abstractions.Stores;

/// <summary>
/// Fluent builder for cache entries. Terminal verbs (<see cref="Get"/>, <see cref="Set"/>,
/// <see cref="Remove"/>, <see cref="Touch"/>, <see cref="Exists"/>, <see cref="GetOrAdd"/>)
/// execute the operation; configuration verbs return the builder for chaining.
/// </summary>
public interface ICacheEntryBuilder<T>
{
    CacheKey Key { get; }

    CacheEntryOptions Options { get; }

    ICacheEntryBuilder<T> WithOptions(Func<CacheEntryOptions, CacheEntryOptions> configure);

    ICacheEntryBuilder<T> WithAbsoluteTtl(TimeSpan ttl);

    ICacheEntryBuilder<T> WithSlidingTtl(TimeSpan ttl);

    ICacheEntryBuilder<T> AllowStaleFor(TimeSpan duration);

    ICacheEntryBuilder<T> WithTags(params string[] tags);

    ICacheEntryBuilder<T> WithContentKind(CacheContentKind kind);

    /// <summary>
    /// Toggle coherence broadcast on writes through this builder. Default is on
    /// (writes broadcast invalidations to peer nodes when a coherence channel is registered).
    /// </summary>
    ICacheEntryBuilder<T> BroadcastInvalidation(bool value = true);

    ICacheEntryBuilder<T> WithConsistency(CacheConsistencyMode mode);

    ValueTask<T?> Get(CancellationToken ct);

    ValueTask<T?> GetOrAdd(Func<CancellationToken, ValueTask<T?>> valueFactory, CancellationToken ct);

    ValueTask Set(T value, CancellationToken ct);

    ValueTask Remove(CancellationToken ct);

    ValueTask Touch(CancellationToken ct);

    ValueTask<bool> Exists(CancellationToken ct);
}
