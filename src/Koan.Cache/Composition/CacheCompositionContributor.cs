using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Coherence;
using Koan.Cache.Entity;
using Koan.Cache.Options;
using Koan.Cache.Policies;
using Koan.Cache.Topology;
using Koan.Core.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Cache.Composition;

/// <summary>Projects Cache topology, peer invalidation, and policy posture into shared runtime facts.</summary>
internal static class CacheCompositionFacts
{
    public static void Project(KoanCompositionBuilder builder, IServiceProvider services, string source)
    {
        services.GetService<CachePolicyBootstrapper>()?.EnsureInitialized();

        var topology = services.GetService<CacheTopology>();
        var coordinator = services.GetService<CoherenceCoordinator>();
        var options = services.GetService<IOptions<CacheOptions>>()?.Value;
        var policies = services.GetService<CachePolicyRegistry>();
        var entityPlans = services.GetService<EntityCachePlan>()?.ResolveAll() ?? [];
        if (topology is null || coordinator is null || options is null) return;

        var topologySelection = topology switch
        {
            { IsLayered: true } => "layered",
            { IsLocalOnly: true } => "local-only",
            { IsRemoteOnly: true } => "remote-only",
            _ => "none"
        };
        builder.AddElection(
            "cache:topology",
            topologySelection,
            "store-capability-resolution",
            source: source,
            factCode: "koan.cache.topology.selected");
        if (topology.LocalReceipt is not null)
            builder.AddElection(
                topology.LocalReceipt,
                source: source,
                factCode: "koan.cache.local.selected");
        if (topology.RemoteReceipt is not null)
            builder.AddElection(
                topology.RemoteReceipt,
                source: source,
                factCode: "koan.cache.remote.selected");
        builder.AddObservation(
            "koan.cache.topology.bounds",
            "cache:topology:bounds",
            $"Cache topology is {topologySelection}: L1={topology.Local?.Name ?? "none"}, " +
            $"L2={topology.Remote?.Name ?? "none"}.",
            "resolved-cache-stores",
            source);

        builder.AddElection(
            "cache:coherence",
            coordinator.ProviderId,
            coordinator.IsActive ? "layered-peer-invalidation" : "inactive-for-topology-or-policy",
            source: source,
            factCode: "koan.cache.coherence.selected");
        builder.AddCapability(
            "cache:coherence",
            [
                "cache.peer-l1-only",
                "cache.origin-filtered",
                "cache.l1-ttl-staleness-bound",
                "cache.communication-node-broadcast"
            ]);
        builder.AddObservation(
            "koan.cache.coherence.posture",
            "cache:coherence:posture",
            $"Peer invalidation is {(coordinator.IsActive ? "active" : "inactive")}: " +
            $"mode={options.CoherenceMode}, provider={coordinator.ProviderId}, assurance={coordinator.Assurance}; " +
            "receivers evict L1 only and lost signals remain bounded by L1 TTL.",
            options.CoherenceMode == CoherenceMode.Required ? "required" : "configured-posture",
            source);

        builder.AddConfigKey(CacheConstants.Configuration.LocalProvider);
        builder.AddConfigKey(CacheConstants.Configuration.RemoteProvider);
        builder.AddConfigKey(CacheConstants.Configuration.CoherenceMode);
        builder.AddConfigKey(CacheConstants.Configuration.DefaultRegionKey);
        builder.AddObservation(
            "koan.cache.policies.discovered",
            "cache:policies",
            $"Koan materialized {policies?.GetAllPolicies().Count ?? 0} cache policy declaration(s) and resolved " +
            $"{entityPlans.Count} Entity entry plan(s): " +
            $"{entityPlans.Count(static plan => plan.ExclusionReason is null)} active, " +
            $"{entityPlans.Count(static plan => plan.ExclusionReason is not null)} safety-excluded.",
            "typed-policy-discovery",
            source);

        foreach (var plan in entityPlans)
        {
            var typeName = plan.EntityType.FullName ?? plan.EntityType.Name;
            var subject = $"cache:entity-plan:{typeName}";
            if (plan.ExclusionReason is null)
            {
                builder.AddObservation(
                    "koan.cache.entity-plan.resolved",
                    subject,
                    $"{typeName} uses {plan.Policy.Strategy} Entity entry caching with key template " +
                    $"'{plan.Policy.KeyTemplate}'; hard segmentation is applied by CacheIdentityPlan.",
                    "entity-cache-plan",
                    source);
            }
            else
            {
                builder.AddObservation(
                    "koan.cache.entity-plan.excluded",
                    subject,
                    $"{typeName} is excluded from Entity entry caching because {plan.ExclusionReason}.",
                    "cache-safety-exclusion",
                    source);
            }
        }
    }
}
