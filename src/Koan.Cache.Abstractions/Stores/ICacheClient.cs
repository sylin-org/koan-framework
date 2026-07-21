using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Abstractions.Stores;

/// <summary>
/// Typed entry point to the cache pillar. Composes read, write, scope, fluent-builder, and
/// tag-flush surfaces. The <c>Store</c> property is intentionally absent — direct store
/// access bypasses the compiled topology and breaks its invariants. Use the builder's
/// <c>WithTier</c> semantic when an operation intentionally targets one tier.
/// </summary>
public interface ICacheClient : ICacheReader, ICacheWriter
{
    ICacheEntryBuilder<T> CreateEntry<T>(CacheKey key);

    CacheScopeHandle BeginScope(string scopeId, string? region = null);

    ValueTask<long> FlushTags(IReadOnlyCollection<string> tags, CancellationToken ct);

    ValueTask<long> CountTags(IReadOnlyCollection<string> tags, CancellationToken ct);
}
