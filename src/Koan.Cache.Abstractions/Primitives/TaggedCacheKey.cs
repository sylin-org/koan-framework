using System;

namespace Koan.Cache.Abstractions.Primitives;

public sealed record TaggedCacheKey(string Tag, CacheKey Key, DateTimeOffset? AbsoluteExpiration)
{
    public bool IsExpired(DateTimeOffset now)
        => AbsoluteExpiration is { } expiration && expiration <= now;
}
