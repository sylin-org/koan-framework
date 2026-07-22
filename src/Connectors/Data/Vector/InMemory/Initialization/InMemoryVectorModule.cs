using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Vector.Connector.InMemory.Initialization;

/// <summary>
/// Auto-registers the in-memory vector adapter when the package is referenced (Reference = Intent).
/// The factory holds the per-(entity, partition) stores, so a single registered singleton is the whole
/// adapter — no options, no discovery, no infrastructure.
/// </summary>
public sealed class InMemoryVectorModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddSingleton<IVectorAdapterFactory, InMemoryVectorAdapterFactory>();
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddSetting("Vector", "InMemory (brute-force, System.Numerics.Tensors SIMD)");
        module.AddSetting("Storage", "in-process, ephemeral");
        module.AddSetting("Priority", $"{Infrastructure.Constants.Provider.Priority} (fallback floor)");
    }
}
