using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Koan.Cache.Abstractions.Primitives;

public sealed record CacheEntryOptions
{
    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>();

    public TimeSpan? AbsoluteTtl { get; init; }
    public TimeSpan? SlidingTtl { get; init; }
    public TimeSpan? AllowStaleFor { get; init; }
    public TimeSpan? SingleflightTimeout { get; init; }
    public CacheConsistencyMode Consistency { get; init; } = CacheConsistencyMode.StaleWhileRevalidate;
    public bool ForcePublishInvalidation { get; init; }
    public IReadOnlySet<string> Tags { get; init; } = EmptySet;
    public CacheContentKind ContentKind { get; init; } = CacheContentKind.Json;
    public string? Region { get; init; }
    public string? ScopeId { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    public DateTimeOffset? CalculateAbsoluteExpiration(DateTimeOffset now)
    {
        if (AbsoluteTtl is { } ttl)
        {
            return now.Add(ttl);
        }

        return null;
    }

    public CacheEntryOptions WithTags(params string[] tags)
    {
        if (tags is null || tags.Length == 0)
        {
            return this;
        }

        var set = Tags is HashSet<string> existing
            ? new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(Tags, StringComparer.OrdinalIgnoreCase);

        foreach (var tag in tags)
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                set.Add(tag.Trim());
            }
        }

        return this with { Tags = set };
    }

    public CacheEntryOptions WithMetadata(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return this;
        }

        var dict = Metadata is ConcurrentDictionary<string, string> concurrent
            ? new ConcurrentDictionary<string, string>(concurrent, StringComparer.Ordinal)
            : new ConcurrentDictionary<string, string>(Metadata, StringComparer.Ordinal);

        dict[key] = value;
        return this with { Metadata = dict };
    }
}
