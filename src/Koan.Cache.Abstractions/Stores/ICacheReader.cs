using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Abstractions.Stores;

public interface ICacheReader
{
    ValueTask<CacheFetchResult> GetAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct);

    ValueTask<T?> GetAsync<T>(CacheKey key, CacheEntryOptions options, CancellationToken ct);

    ValueTask<T?> GetOrAddAsync<T>(CacheKey key, Func<CancellationToken, ValueTask<T?>> valueFactory, CacheEntryOptions options, CancellationToken ct);

    ValueTask<bool> ExistsAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct);
}
