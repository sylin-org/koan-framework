using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Abstractions.Stores;

/// <summary>
/// Typed entry point to the cache pillar. Composes read, write, scope, fluent-builder, and
/// tag-flush surfaces. The <c>Store</c> property is intentionally absent — direct store
/// access bypasses the layered cache and breaks topology invariants. Use
/// <c>ICacheStoreRegistry</c> if you genuinely need a single-tier handle.
/// </summary>
public interface ICacheClient : ICacheReader, ICacheWriter
{
    ICacheEntryBuilder<T> CreateEntry<T>(CacheKey key);

    CacheScopeHandle BeginScope(string scopeId, string? region = null);

    ValueTask<long> FlushTags(IReadOnlyCollection<string> tags, CancellationToken ct);

    ValueTask<long> CountTags(IReadOnlyCollection<string> tags, CancellationToken ct);
}
