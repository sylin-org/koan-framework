using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Abstractions.Stores;

public interface ICacheWriter
{
    ValueTask SetAsync<T>(CacheKey key, T value, CacheEntryOptions options, CancellationToken ct);

    ValueTask<bool> RemoveAsync(CacheKey key, CancellationToken ct);

    ValueTask TouchAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct);
}
