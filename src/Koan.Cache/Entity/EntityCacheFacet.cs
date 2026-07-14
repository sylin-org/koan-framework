using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Model;

/// <summary>
/// Adds the type-level <c>Cache</c> facet to Koan entities when, and only when, the
/// <c>Koan.Cache</c> module is referenced.
/// </summary>
public static class EntityCacheFacetExtensions
{
    extension<TEntity, TKey>(Entity<TEntity, TKey>)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        /// <summary>
        /// Gets cache policy inspection and entity-type-scoped cache operations for
        /// <typeparamref name="TEntity"/>. Operations resolve the active host when invoked and
        /// never retain services from an earlier host.
        /// </summary>
        public static EntityCacheFacet<TEntity, TKey> Cache => default;
    }
}

/// <summary>
/// Cache policy inspection and compatibility operations scoped to one Entity type.
/// This is not a cache-cluster administration surface.
/// </summary>
public readonly struct EntityCacheFacet<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private const string ExplainOperation = "entity cache policy explanation";
    private const string ControlOperation = "entity-type cache control";

    /// <summary>
    /// Explains the materialized cache policies for <typeparamref name="TEntity"/> without reading,
    /// writing, or evicting cache entries. Requires an active host with <c>Koan.Cache</c> composed.
    /// </summary>
    public EntityCacheExplanation Explain()
    {
        var registry = AppHost.GetRequiredService<ICachePolicyRegistry>(ExplainOperation);
        var policies = registry.GetPoliciesFor(typeof(TEntity))
            .Select(EntityCachePolicyExplanation.From)
            .ToArray();

        return new EntityCacheExplanation(
            CacheConstants.Capabilities.Entity,
            typeof(TEntity),
            policies);
    }

    /// <summary>
    /// Removes cache entries carrying the concrete policy tags for this Entity type.
    /// Returns the number of entries removed.
    /// </summary>
    public ValueTask<long> Flush(CancellationToken ct = default)
        => FlushInternal([], ct);

    /// <summary>
    /// Removes cache entries carrying this Entity type's concrete policy tags plus the supplied tags.
    /// Returns the number of entries removed.
    /// </summary>
    public ValueTask<long> Flush(IEnumerable<string> tags, CancellationToken ct = default)
        => FlushInternal(tags, ct);

    /// <summary>
    /// Removes cache entries carrying this Entity type's concrete policy tags plus the supplied tag.
    /// Returns the number of entries removed.
    /// </summary>
    public ValueTask<long> Flush(string tag, CancellationToken ct = default)
        => FlushInternal([tag], ct);

    /// <summary>Counts entries carrying the concrete policy tags for this Entity type.</summary>
    public ValueTask<long> Count(CancellationToken ct = default)
        => CountInternal([], ct);

    /// <summary>Counts entries carrying this Entity type's concrete policy tags plus the supplied tags.</summary>
    public ValueTask<long> Count(IEnumerable<string> tags, CancellationToken ct = default)
        => CountInternal(tags, ct);

    /// <summary>Counts entries carrying this Entity type's concrete policy tags plus the supplied tag.</summary>
    public ValueTask<long> Count(string tag, CancellationToken ct = default)
        => CountInternal([tag], ct);

    /// <summary>Reports whether any entry carries the concrete policy tags for this Entity type.</summary>
    public async ValueTask<bool> Any(CancellationToken ct = default)
        => await CountInternal([], ct).ConfigureAwait(false) > 0;

    /// <summary>Reports whether any entry carries this Entity type's policy tags plus the supplied tags.</summary>
    public async ValueTask<bool> Any(IEnumerable<string> tags, CancellationToken ct = default)
        => await CountInternal(tags, ct).ConfigureAwait(false) > 0;

    private static ValueTask<long> FlushInternal(IEnumerable<string>? tags, CancellationToken ct)
    {
        var resolved = ResolveTags(tags);
        if (resolved.Count == 0)
        {
            return ValueTask.FromResult(0L);
        }

        return AppHost.GetRequiredService<ICacheClient>(ControlOperation).FlushTags(resolved, ct);
    }

    private static ValueTask<long> CountInternal(IEnumerable<string>? tags, CancellationToken ct)
    {
        var resolved = ResolveTags(tags);
        if (resolved.Count == 0)
        {
            return ValueTask.FromResult(0L);
        }

        return AppHost.GetRequiredService<ICacheClient>(ControlOperation).CountTags(resolved, ct);
    }

    private static IReadOnlyCollection<string> ResolveTags(IEnumerable<string>? additionalTags)
    {
        var registry = AppHost.GetRequiredService<ICachePolicyRegistry>(ControlOperation);
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in registry.GetPoliciesFor(typeof(TEntity)))
        {
            if (descriptor.Scope is not (CacheScope.Entity or CacheScope.EntityQuery))
            {
                continue;
            }

            foreach (var tag in descriptor.Tags)
            {
                if (IsConcrete(tag))
                {
                    tags.Add(tag.Trim());
                }
            }
        }

        if (additionalTags is not null)
        {
            foreach (var tag in additionalTags)
            {
                if (IsConcrete(tag))
                {
                    tags.Add(tag.Trim());
                }
            }
        }

        return tags.Count == 0 ? [] : tags.ToArray();
    }

    private static bool IsConcrete(string? tag)
        => !string.IsNullOrWhiteSpace(tag) && tag.IndexOf('{') < 0;
}

/// <summary>
/// Read-only, provider-neutral explanation of the cache policies materialized for one Entity type.
/// </summary>
/// <param name="Capability">Stable capability identity for logs, documentation, and future projections.</param>
/// <param name="EntityType">Entity type whose cache policy was inspected.</param>
/// <param name="Policies">Materialized policy facts; empty means the Entity has no cache policy.</param>
public sealed record EntityCacheExplanation(
    string Capability,
    Type EntityType,
    IReadOnlyList<EntityCachePolicyExplanation> Policies)
{
    /// <summary>Whether at least one cache policy is materialized for this Entity type.</summary>
    public bool IsConfigured => Policies.Count > 0;

    /// <summary>Returns a concise human-readable policy summary.</summary>
    public string Summary()
        => Policies.Count switch
        {
            0 => $"{EntityType.Name}: no cache policy configured",
            1 => $"{EntityType.Name}: 1 cache policy configured",
            _ => $"{EntityType.Name}: {Policies.Count} cache policies configured"
        };
}

/// <summary>Safe projection of one materialized Entity cache policy.</summary>
public sealed record EntityCachePolicyExplanation(
    CacheScope Scope,
    CacheStrategy Strategy,
    CacheConsistencyMode Consistency,
    CacheTier Tier,
    TimeSpan? AbsoluteTtl,
    TimeSpan? LocalAbsoluteTtl,
    TimeSpan? SlidingTtl,
    TimeSpan? AllowStaleFor,
    IReadOnlyList<string> Tags,
    string? Region,
    string? LocalProvider,
    string? RemoteProvider,
    bool BroadcastInvalidations)
{
    internal static EntityCachePolicyExplanation From(CachePolicyDescriptor policy)
        => new(
            policy.Scope,
            policy.Strategy,
            policy.Consistency,
            policy.Tier,
            policy.AbsoluteTtl,
            policy.L1AbsoluteTtl,
            policy.SlidingTtl,
            policy.AllowStaleFor,
            policy.Tags.ToArray(),
            policy.Region,
            policy.LocalProvider,
            policy.RemoteProvider,
            policy.ForceCoherenceBroadcast);
}
