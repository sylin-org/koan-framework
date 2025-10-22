using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Abstractions.Stores;

public interface ICacheStore
{
    string ProviderName { get; }

    CacheCapabilities Capabilities { get; }

    ValueTask<CacheFetchResult> FetchAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct);

    ValueTask SetAsync(CacheKey key, CacheValue value, CacheEntryOptions options, CancellationToken ct);

    ValueTask<bool> RemoveAsync(CacheKey key, CancellationToken ct);

    ValueTask TouchAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct);

    ValueTask<bool> ExistsAsync(CacheKey key, CancellationToken ct);

    ValueTask PublishInvalidationAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct);

    IAsyncEnumerable<TaggedCacheKey> EnumerateByTagAsync(string tag, CancellationToken ct);
}
