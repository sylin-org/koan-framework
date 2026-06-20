using System;
using System.Collections.Generic;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Abstractions.Policies;

/// <summary>
/// Declarative cache intent. Applied at class, struct, or method scope. Discovered at
/// startup by <c>CachePolicyBootstrapper</c> and materialized into
/// <see cref="CachePolicyDescriptor"/> for runtime use.
/// </summary>
/// <remarks>
/// <para>
/// Not <c>sealed</c> so <see cref="CacheableAttribute"/> can specialize it with entity-friendly
/// defaults. Power users who need controller-action caching, method-level policies, or custom
/// key templates apply this attribute directly.
/// </para>
/// <para>
/// TTL fields are declared as <c>TimeSpan?</c> for runtime flexibility. C# attribute syntax
/// cannot construct <c>TimeSpan</c> literals, so consumers either set them in code via the
/// descriptor, or use <see cref="CacheableAttribute"/>'s integer-second sister setters.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Struct, Inherited = true, AllowMultiple = true)]
public class CachePolicyAttribute : Attribute
{
    private static readonly string[] EmptyTags = [];

    public CachePolicyAttribute(CacheScope scope, string keyTemplate)
    {
        Scope = scope;
        KeyTemplate = string.IsNullOrWhiteSpace(keyTemplate)
            ? throw new ArgumentException("Key template must be provided.", nameof(keyTemplate))
            : keyTemplate;
    }

    /// <summary>Scope this policy applies to (Entity, ControllerAction, ControllerResponse, etc.).</summary>
    public CacheScope Scope { get; }

    /// <summary>Key template with <c>{Id}</c>, <c>{TypeName}</c>, <c>{Partition}</c>, <c>{Entity.Property}</c> placeholders.</summary>
    public string KeyTemplate { get; }

    /// <summary>Read/write behavior. Default <c>GetOrSet</c> (read-through, write-back).</summary>
    public CacheStrategy Strategy { get; set; } = CacheStrategy.GetOrSet;

    /// <summary>Consistency mode when cache is unavailable or stale. Default <c>StaleWhileRevalidate</c>.</summary>
    public CacheConsistencyMode Consistency { get; set; } = CacheConsistencyMode.StaleWhileRevalidate;

    /// <summary>Which tiers to use. Default <c>Layered</c> (L1 + L2 with auto-fallback).</summary>
    public CacheTier Tier { get; set; } = CacheTier.Layered;

    /// <summary>Absolute expiration. Set via <c>CacheableAttribute</c>'s <c>ttlSeconds</c> in attribute syntax.</summary>
    public TimeSpan? AbsoluteTtl { get; set; }

    /// <summary>L1-specific TTL override. Null = derive <c>max(30s, AbsoluteTtl/2)</c> at write time.</summary>
    public TimeSpan? L1AbsoluteTtl { get; set; }

    /// <summary>Sliding expiration window. Refreshed on each read when supported.</summary>
    public TimeSpan? SlidingTtl { get; set; }

    /// <summary>How long to serve stale data while a background refresh runs. SWR consistency only.</summary>
    public TimeSpan? AllowStaleFor { get; set; }

    /// <summary>Tags applied to entries created under this policy. Used for bulk invalidation.</summary>
    public string[] Tags { get; set; } = EmptyTags;

    /// <summary>Optional region scoping for tenant isolation.</summary>
    public string? Region { get; set; }

    /// <summary>Optional scope-id for fine-grained isolation within a region.</summary>
    public string? ScopeId { get; set; }

    /// <summary>Pin L1 (local) tier to a specific store by <c>ICacheStore.Name</c>. Null = let the resolver pick.</summary>
    public string? LocalProvider { get; set; }

    /// <summary>Pin L2 (remote) tier to a specific store by <c>ICacheStore.Name</c>. Null = let the resolver pick.</summary>
    public string? RemoteProvider { get; set; }

    /// <summary>Whether writes under this policy broadcast a coherence message. Default <c>true</c>.</summary>
    public bool ForceCoherenceBroadcast { get; set; } = true;

    /// <summary>Arbitrary policy metadata, available on the descriptor at runtime.</summary>
    public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
}
