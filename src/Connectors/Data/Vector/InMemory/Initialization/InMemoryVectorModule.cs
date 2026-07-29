using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Vector.Connector.InMemory.Initialization;

/// <summary>Makes the exact ephemeral Vector floor available through ordinary AddKoan().</summary>
public sealed class InMemoryVectorModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<InMemoryVectorOptions>(Infrastructure.Constants.Configuration.Section);
        services.AddSingleton<IVectorAdapterFactory, InMemoryVectorAdapterFactory>();
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddSetting("Vector", "InMemory exact brute-force");
        module.AddSetting("Storage", "host-owned, bounded, ephemeral");
        module.AddSetting("Priority", $"{Infrastructure.Constants.Provider.Priority} (automatic floor)");
    }
}
