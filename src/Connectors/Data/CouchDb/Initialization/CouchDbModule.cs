using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.CouchDb.Discovery;
using Koan.Data.Connector.CouchDb.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.CouchDb.Initialization;

public sealed class CouchDbModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<CouchDbOptions>(Infrastructure.Constants.Configuration.Section);
        services.AddSingleton<IConfigureOptions<CouchDbOptions>, CouchDbOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.AddSingleton<CouchDbClientManager>();
        services.AddSingleton<CouchDbAdapterFactory>();
        services.AddSingleton<IDataAdapterFactory>(static provider => provider.GetRequiredService<CouchDbAdapterFactory>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, CouchDbHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, CouchDbDiscoveryAdapter>());
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        module.Describe(Version);
        module.AddNote("CouchDB provides one document execution path over plain HTTP for managed and externally mapped sources.");
    }
}
