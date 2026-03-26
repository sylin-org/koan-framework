using System;
using System.Collections.Generic;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;

namespace Koan.Cache.Abstractions.Policies;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Struct, Inherited = true, AllowMultiple = true)]
public sealed class CachePolicyAttribute : Attribute
{
    private static readonly string[] EmptyTags = [];

    public CachePolicyAttribute(CacheScope scope, string keyTemplate)
    {
        Scope = scope;
        KeyTemplate = string.IsNullOrWhiteSpace(keyTemplate)
            ? throw new ArgumentException("Key template must be provided.", nameof(keyTemplate))
            : keyTemplate;
    }

    public CacheScope Scope { get; }

    public string KeyTemplate { get; }

    public CacheStrategy Strategy { get; init; } = CacheStrategy.GetOrSet;

    public CacheConsistencyMode Consistency { get; init; } = CacheConsistencyMode.StaleWhileRevalidate;

    public TimeSpan? AbsoluteTtl { get; init; }

    public TimeSpan? SlidingTtl { get; init; }

    public TimeSpan? AllowStaleFor { get; init; }

    public bool ForcePublishInvalidation { get; init; }

    public string[] Tags { get; init; } = EmptyTags;

    public string? Region { get; init; }

    public string? ScopeId { get; init; }

    /// <summary>
    /// Pins this cache policy to a specific cache provider type.
    /// When set, the cache system routes entries through the specified provider
    /// instead of the default registered <see cref="Koan.Cache.Abstractions.Stores.ICacheStore"/>.
    /// </summary>
    public Type? Provider { get; set; }

    public IDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Controls which cache tiers are used. Default: Layered (L1 + L2).
    /// </summary>
    public CacheTier Tier { get; init; } = CacheTier.Layered;

    /// <summary>
    /// Pin L1 (local) tier to a specific provider type. Null = auto-detect.
    /// </summary>
    public Type? LocalProvider { get; init; }

    /// <summary>
    /// Pin L2 (remote) tier to a specific provider type. Null = auto-detect.
    /// </summary>
    public Type? RemoteProvider { get; init; }
}
