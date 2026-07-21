using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Data.Abstractions;
using Koan.Data.Connector.InMemory.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Connector.InMemory.Initialization;

/// <summary>
/// Makes the host-scoped InMemory adapter available when its package is referenced.
/// </summary>
public sealed class InMemoryDataModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        // One host owns one ephemeral store; DataService owns repository reuse within that host.
        services.AddSingleton<InMemoryDataStore>();
        services.AddSingleton<IDataAdapterFactory, InMemoryAdapterFactory>();
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddSetting(Constants.Bootstrap.Storage, "InMemory (host-scoped, ephemeral)");
        module.AddSetting(Constants.Bootstrap.Priority, Constants.Provider.Priority.ToString());
        module.AddNote("Direct provider: reference or configure intentionally; JSON remains the automatic local floor.");
        module.AddNote("AODB isolation: RowScoped + ContainerScoped + DatabaseScoped (conformance: AodbConformanceSpecsBase)");
    }
}
