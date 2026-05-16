using System;
using System.Collections.Generic;
using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Adapter.Redis.Coherence;

/// <summary>
/// Wire-format envelope for <see cref="CacheInvalidation"/> over Redis pub/sub.
/// The strongly-typed value type isn't directly JSON-friendly (record struct with nullable
/// reference-type members); this DTO mirrors it for serialization.
/// </summary>
internal sealed record RedisInvalidationEnvelope
{
    public string Kind { get; init; } = "";
    public string? Key { get; init; }
    public string[]? Tags { get; init; }
    public string? Region { get; init; }
    public string? ScopeId { get; init; }
    public string OriginNodeId { get; init; } = "";
    public long PublishedAtUtcTicks { get; init; }

    public static RedisInvalidationEnvelope FromMessage(CacheInvalidation msg)
        => new()
        {
            Kind = msg.Kind.ToString(),
            Key = msg.Key?.Value,
            Tags = msg.Tags is { Count: > 0 } tags ? System.Linq.Enumerable.ToArray(tags) : null,
            Region = msg.Region,
            ScopeId = msg.ScopeId,
            OriginNodeId = msg.OriginNodeId.ToString("D"),
            PublishedAtUtcTicks = msg.PublishedAtUtc.UtcTicks,
        };

    public CacheInvalidation ToMessage()
    {
        var kind = Enum.TryParse<CacheInvalidationKind>(Kind, ignoreCase: true, out var parsed)
            ? parsed
            : CacheInvalidationKind.EvictKey;

        var originNodeId = Guid.TryParse(OriginNodeId, out var guid) ? guid : Guid.Empty;
        var publishedAt = new DateTimeOffset(PublishedAtUtcTicks, TimeSpan.Zero);

        IReadOnlySet<string>? tags = Tags is { Length: > 0 }
            ? new HashSet<string>(Tags, StringComparer.OrdinalIgnoreCase)
            : null;

        // Cast to CacheKey? on both branches — without it, the compiler picks the implicit
        // string→CacheKey operator path for `null`, which throws.
        CacheKey? key = Key is not null ? (CacheKey?)new CacheKey(Key) : null;

        return new CacheInvalidation(kind, key, tags, Region, ScopeId, originNodeId, publishedAt);
    }
}
