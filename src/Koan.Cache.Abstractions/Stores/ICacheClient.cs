using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Abstractions.Stores;

public interface ICacheClient : ICacheReader, ICacheWriter
{
    ICacheStore Store { get; }

    ICacheEntryBuilder<T> CreateEntry<T>(CacheKey key);

    CacheScopeHandle BeginScope(string scopeId, string? region = null);

    ValueTask<long> FlushTagsAsync(IReadOnlyCollection<string> tags, CancellationToken ct);

    ValueTask<long> CountTagsAsync(IReadOnlyCollection<string> tags, CancellationToken ct);
}
