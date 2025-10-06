using System;

namespace Koan.Cache.Abstractions.Primitives;

public sealed record CacheFetchResult
{
    public bool Hit { get; init; }
    public CacheValue? Value { get; init; }
    public CacheEntryOptions Options { get; init; } = new();
    public DateTimeOffset? AbsoluteExpiration { get; init; }
    public DateTimeOffset? StaleUntil { get; init; }

    public static CacheFetchResult Miss(CacheEntryOptions options)
        => new() { Hit = false, Options = options };

    public static CacheFetchResult HitResult(CacheValue value, CacheEntryOptions options, DateTimeOffset? absoluteExpiration, DateTimeOffset? staleUntil)
        => new()
        {
            Hit = true,
            Value = value,
            Options = options,
            AbsoluteExpiration = absoluteExpiration,
            StaleUntil = staleUntil
        };
}
