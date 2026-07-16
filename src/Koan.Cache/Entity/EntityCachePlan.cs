using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Keys;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core;
using Koan.Data.Core.Pipeline;

namespace Koan.Cache.Entity;

/// <summary>
/// Owns the one host-level decision for whether and how an Entity cache entry exists.
/// Repository decoration and explicit eviction consume the same resolved plan.
/// </summary>
internal sealed class EntityCachePlan(
    ICachePolicyRegistry policyRegistry,
    IEnumerable<IReadFilterContributor> readContributors)
{
    private readonly IReadOnlyList<IReadFilterContributor> _readContributors = readContributors.ToArray();

    /// <summary>Resolves every declared Entity entry policy once, in deterministic type order.</summary>
    public IReadOnlyList<Resolution> ResolveAll()
        => policyRegistry.GetAllPolicies()
            .Where(static policy =>
                policy.DeclaringType is not null
                && policy.Scope == CacheScope.Entity
                && policy.Strategy != CacheStrategy.NoCache)
            .Select(static policy => policy.DeclaringType!)
            .Distinct()
            .OrderBy(static type => type.FullName ?? type.Name, StringComparer.Ordinal)
            .Select(TryResolve)
            .OfType<Resolution>()
            .ToArray();

    public Resolution? TryResolve(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        var policy = policyRegistry.GetPoliciesFor(entityType)
            .FirstOrDefault(static candidate =>
                candidate.Scope == CacheScope.Entity && candidate.Strategy != CacheStrategy.NoCache);
        if (policy is null)
        {
            return null;
        }

        return new Resolution(
            entityType,
            policy,
            CacheKeyTemplate.For(policy.KeyTemplate),
            ResolveExclusion(entityType));
    }

    public Resolution Require(Type entityType)
    {
        var plan = TryResolve(entityType)
            ?? throw new InvalidOperationException(
                $"Entity '{entityType.Name}' has no active Entity cache policy. " +
                "Add [Cacheable] or an Entity-scoped [CachePolicy] before requesting entry eviction.");

        if (plan.ExclusionReason is not null)
        {
            throw new InvalidOperationException(
                $"Entity '{entityType.Name}' is excluded from caching because {plan.ExclusionReason}. " +
                "No cache entry can exist, so explicit entry eviction is unavailable.");
        }

        return plan;
    }

    private string? ResolveExclusion(Type entityType)
    {
        if (StorageFieldTransformRegistry.HasTransformsFor(entityType))
        {
            return "its stored representation is transformed before the Data facade restores it";
        }

        if (!ManagedFieldRegistry.IsEmpty)
        {
            var managed = ManagedFieldRegistry.ForType(entityType);
            for (var index = 0; index < managed.Count; index++)
            {
                if (!managed[index].AutoReadFilter)
                {
                    return $"read-scope axis '{managed[index].StorageName}' is not an equality key segment";
                }
            }
        }

        foreach (var contributor in _readContributors)
        {
            if (contributor.ExcludesFromCache(entityType))
            {
                return "a read-scope predicate cannot be represented by an equality cache key";
            }
        }

        return null;
    }

    internal sealed class Resolution(
        Type entityType,
        CachePolicyDescriptor policy,
        CacheKeyTemplate template,
        string? exclusionReason)
    {
        public Type EntityType { get; } = entityType;

        public string EntityName { get; } = CacheKey.EntityTypeName(entityType);

        public CachePolicyDescriptor Policy { get; } = policy;

        public string? ExclusionReason { get; } = exclusionReason;

        public bool TryBuildKey(object? entity, object? id, out CacheKey key)
        {
            var context = EntityContext.Current;
            var ambient = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Id"] = id,
                ["Key"] = id,
                ["TypeName"] = EntityName,
                ["Partition"] = string.IsNullOrWhiteSpace(context?.Partition) ? "_" : context.Partition,
                ["Source"] = string.IsNullOrWhiteSpace(context?.Source) ? "_" : context.Source,
            };

            if (entity is not null)
            {
                ambient["Entity"] = entity;
            }

            var formatted = template.TryFormat(entity, ambient, out _);
            if (formatted is null)
            {
                key = default;
                return false;
            }

            key = new CacheKey(ScopedEntityCacheKey.AppendScope(formatted, EntityType));
            return true;
        }
    }
}
