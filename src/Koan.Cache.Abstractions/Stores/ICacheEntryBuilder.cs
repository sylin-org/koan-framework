using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Abstractions.Stores;

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

    ICacheEntryBuilder<T> PublishInvalidation(bool value = true);

    ICacheEntryBuilder<T> WithConsistency(CacheConsistencyMode mode);

    ValueTask<T?> GetAsync(CancellationToken ct);

    ValueTask<T?> GetOrAddAsync(Func<CancellationToken, ValueTask<T?>> valueFactory, CancellationToken ct);

    ValueTask SetAsync(T value, CancellationToken ct);

    ValueTask RemoveAsync(CancellationToken ct);

    ValueTask TouchAsync(CancellationToken ct);

    ValueTask<bool> Exists(CancellationToken ct);
}
