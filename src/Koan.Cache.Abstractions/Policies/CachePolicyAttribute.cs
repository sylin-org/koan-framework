using System;
using System.Collections.Generic;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Abstractions.Policies;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Struct, Inherited = true, AllowMultiple = true)]
public sealed class CachePolicyAttribute : Attribute
{
    private static readonly string[] EmptyTags = Array.Empty<string>();

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

    public IDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
