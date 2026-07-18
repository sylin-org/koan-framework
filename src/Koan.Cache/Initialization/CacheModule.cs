using Koan.Cache.Abstractions;
using Koan.Cache.Diagnostics;
using Koan.Cache.Pillars;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Semantics;
using Koan.Core.Composition;
using Koan.Core.Provenance;
using Koan.Cache.Composition;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Cache.Initialization;

/// <summary>Reference = Intent registration for the Cache pillar.</summary>
public sealed class CacheModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        CachingPillarManifest.EnsureRegistered();
        CacheServices.Register(services);
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);

        var defaultRegion = Configuration.ReadWithSource(
            cfg,
            CacheConstants.Configuration.DefaultRegionKey,
            CacheConstants.Configuration.DefaultRegion);
        var singleflightTimeout = Configuration.ReadWithSource(
            cfg,
            CacheConstants.Configuration.DefaultSingleflightTimeout,
            TimeSpan.FromSeconds(5));
        var defaultTier = Configuration.ReadWithSource(
            cfg,
            CacheConstants.Configuration.DefaultTier,
            Abstractions.Primitives.CacheTier.Layered);
        var defaultTtl = Configuration.ReadWithSource(
            cfg,
            CacheConstants.Configuration.DefaultTtlSeconds,
            300);
        var defaultL1Ttl = Configuration.ReadWithSource<int?>(
            cfg,
            CacheConstants.Configuration.DefaultL1TtlSeconds,
            null);
        var broadcastInvalidation = Configuration.ReadWithSource(
            cfg,
            CacheConstants.Configuration.BroadcastInvalidationByDefault,
            true);
        var memoryTagIndexCapacity = Configuration.Read(
            cfg,
            CacheConstants.Configuration.Memory.TagIndexCapacity,
            2048);

        module.AddSetting(
            "DefaultRegion",
            defaultRegion.Value ?? CacheConstants.Configuration.DefaultRegion,
            source: defaultRegion.Source,
            consumers: ["Koan.Cache.RegionResolver"]);
        module.AddSetting(
            "DefaultSingleflightTimeout",
            singleflightTimeout.Value.ToString(),
            source: singleflightTimeout.Source,
            consumers: ["Koan.Cache.Singleflight"]);
        module.AddSetting("DefaultTier", defaultTier.Value.ToString(), source: defaultTier.Source, consumers: ["Koan.Cache.Client"]);
        module.AddSetting("DefaultTtlSeconds", defaultTtl.Value.ToString(), source: defaultTtl.Source, consumers: ["Koan.Cache.Client"]);
        module.AddSetting("DefaultL1TtlSeconds", defaultL1Ttl.Value?.ToString() ?? "derived", source: defaultL1Ttl.Source, consumers: ["Koan.Cache.Layered"]);
        module.AddSetting("BroadcastInvalidationByDefault", broadcastInvalidation.Value.ToString(), source: broadcastInvalidation.Source, consumers: ["Koan.Cache.Coherence"]);
        module.AddSetting("BuiltInProvider", "memory ([ProviderPriority] floor)");
        module.AddSetting("MemoryTagIndexCapacity", memoryTagIndexCapacity.ToString());

        if (CacheTraceFilter.IsEnabled)
            module.AddSetting("TraceKey", CacheTraceFilter.TraceKey!, source: BootSettingSource.Environment);

        module.AddNote("Runtime topology, policy, and coherence elections are reported through Koan composition facts.");
    }

    public override void ReportComposition(KoanCompositionBuilder composition, IServiceProvider services)
        => CacheCompositionFacts.Project(composition, services, GetType().FullName ?? Id);
}
