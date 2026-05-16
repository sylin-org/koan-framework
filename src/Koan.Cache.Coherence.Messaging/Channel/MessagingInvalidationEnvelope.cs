using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Coherence.Messaging.Channel;

/// <summary>
/// Wire DTO for <see cref="CacheInvalidation"/> over <c>Koan.Messaging</c>. The bus requires
/// <c>T : class</c> on send/consume APIs, so the strongly-typed value type needs a
/// reference-type envelope.
/// </summary>
public sealed class MessagingInvalidationEnvelope
{
    public string Kind { get; init; } = "";
    public string? Key { get; init; }
    public string[]? Tags { get; init; }
    public string? Region { get; init; }
    public string? ScopeId { get; init; }
    public string OriginNodeId { get; init; } = "";
    public long PublishedAtUtcTicks { get; init; }

    public static MessagingInvalidationEnvelope FromMessage(CacheInvalidation msg)
        => new()
        {
            Kind = msg.Kind.ToString(),
            Key = msg.Key?.Value,
            Tags = msg.Tags is { Count: > 0 } tags ? tags.ToArray() : null,
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
