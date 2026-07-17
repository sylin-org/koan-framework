using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Canon;

/// <summary>
/// Activates the Canon runtime and compiles discovered model pipelines.
/// </summary>
public sealed class CanonModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        FlowPillarManifest.EnsureRegistered();
        CanonPipelineDiscovery.Register(services);
        services.AddCanonRuntime();
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version, "Entity-first canonicalization runtime");
        module.AddSetting("models", Koan.Core.Hosting.Registry.KoanRegistry
            .GetDiscoveredImplementors(typeof(ICanonModel)).Length.ToString());
        module.AddSetting("contributors", CanonPipelineDiscovery.DiscoverContributorTypes().Count.ToString());
    }
}
