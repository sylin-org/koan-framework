using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Abstractions.Stores;

public interface ICacheStore
{
    string ProviderName { get; }

    CacheCapabilities Capabilities { get; }

    ValueTask<CacheFetchResult> Fetch(CacheKey key, CacheEntryOptions options, CancellationToken ct);

    ValueTask Set(CacheKey key, CacheValue value, CacheEntryOptions options, CancellationToken ct);

    ValueTask<bool> Remove(CacheKey key, CancellationToken ct);

    ValueTask Touch(CacheKey key, CacheEntryOptions options, CancellationToken ct);

    ValueTask<bool> Exists(CacheKey key, CancellationToken ct);

    ValueTask PublishInvalidation(CacheKey key, CacheEntryOptions options, CancellationToken ct);

    IAsyncEnumerable<TaggedCacheKey> EnumerateByTag(string tag, CancellationToken ct);
}
