using System;
using System.Collections.Generic;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Adapter.Redis.Stores;

internal sealed record RedisCacheEnvelope
{
    public string Key { get; init; } = string.Empty;
    public long CreatedUtcTicks { get; init; }
    public long? AbsoluteExpirationUtcTicks { get; init; }
    public long? StaleUntilUtcTicks { get; init; }
    public CacheValueModel Value { get; init; } = new();
    public CacheEntryOptionsModel Options { get; init; } = new();
}

internal sealed record CacheValueModel
{
    public CacheContentKind Kind { get; init; }
    public string? Text { get; init; }
    public byte[]? Binary { get; init; }

    public CacheValue ToCacheValue()
    {
        return Kind switch
        {
            CacheContentKind.Binary => CacheValue.FromBytes(Binary ?? Array.Empty<byte>()),
            CacheContentKind.String => CacheValue.FromString(Text ?? string.Empty),
            CacheContentKind.Json => CacheValue.FromJson(Text ?? string.Empty),
            _ => CacheValue.FromBytes(Binary ?? Array.Empty<byte>())
        };
    }

    public static CacheValueModel FromCacheValue(CacheValue value)
    {
        return value.ContentKind switch
        {
            CacheContentKind.Binary => new CacheValueModel
            {
                Kind = CacheContentKind.Binary,
                Binary = value.ToBytes().ToArray()
            },
            CacheContentKind.String => new CacheValueModel
            {
                Kind = CacheContentKind.String,
                Text = value.ToText()
            },
            CacheContentKind.Json => new CacheValueModel
            {
                Kind = CacheContentKind.Json,
                Text = value.ToText()
            },
            _ => new CacheValueModel
            {
                Kind = CacheContentKind.Binary,
                Binary = value.ToBytes().ToArray()
            }
        };
    }
}

internal sealed record CacheEntryOptionsModel
{
    public long? AbsoluteTtlTicks { get; init; }
    public long? SlidingTtlTicks { get; init; }
    public long? AllowStaleForTicks { get; init; }
    public long? SingleflightTimeoutTicks { get; init; }
    public CacheConsistencyMode Consistency { get; init; }
    public bool ForcePublishInvalidation { get; init; }
    public CacheContentKind ContentKind { get; init; }
    public string[] Tags { get; init; } = Array.Empty<string>();
    public string? Region { get; init; }
    public string? ScopeId { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);

    public CacheEntryOptions ToOptions()
    {
        var metadata = Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var tags = Tags ?? Array.Empty<string>();

        var options = new CacheEntryOptions
        {
            AbsoluteTtl = AbsoluteTtlTicks.HasValue ? TimeSpan.FromTicks(AbsoluteTtlTicks.Value) : null,
            SlidingTtl = SlidingTtlTicks.HasValue ? TimeSpan.FromTicks(SlidingTtlTicks.Value) : null,
            AllowStaleFor = AllowStaleForTicks.HasValue ? TimeSpan.FromTicks(AllowStaleForTicks.Value) : null,
            SingleflightTimeout = SingleflightTimeoutTicks.HasValue ? TimeSpan.FromTicks(SingleflightTimeoutTicks.Value) : null,
            Consistency = Consistency,
            ForcePublishInvalidation = ForcePublishInvalidation,
            ContentKind = ContentKind,
            Region = Region,
            ScopeId = ScopeId,
            Metadata = metadata.Count == 0
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(metadata, StringComparer.Ordinal),
            Tags = tags.Length == 0
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase)
        };

        return options;
    }

    public static CacheEntryOptionsModel FromOptions(CacheEntryOptions options) => new()
    {
        AbsoluteTtlTicks = options.AbsoluteTtl?.Ticks,
        SlidingTtlTicks = options.SlidingTtl?.Ticks,
        AllowStaleForTicks = options.AllowStaleFor?.Ticks,
        SingleflightTimeoutTicks = options.SingleflightTimeout?.Ticks,
        Consistency = options.Consistency,
        ForcePublishInvalidation = options.ForcePublishInvalidation,
        ContentKind = options.ContentKind,
        Region = options.Region,
        ScopeId = options.ScopeId,
        Metadata = options.Metadata is { Count: > 0 } metadata
            ? metadata.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal),
        Tags = options.Tags is { Count: > 0 } tags
            ? tags.ToArray()
            : Array.Empty<string>()
    };
}
