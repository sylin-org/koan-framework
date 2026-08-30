using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Couchbase.Discovery;
using Koan.Data.Connector.Couchbase.Runtime;
using Koan.Data.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Connector.Couchbase.Initialization;

public sealed class CouchbaseModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<CouchbaseOptions>();
        services.AddSingleton<IConfigureOptions<CouchbaseOptions>, CouchbaseOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddSingleton<CouchbaseResourcePool>();
        services.AddSingleton<CouchbaseAdapterFactory>();
        services.AddSingleton<IDataAdapterFactory>(static provider => provider.GetRequiredService<CouchbaseAdapterFactory>());
        services.AddSingleton<IDataSourceIntegrationFactory>(static provider => provider.GetRequiredService<CouchbaseAdapterFactory>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, CouchbaseHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, CouchbaseDiscoveryAdapter>());
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        module.Describe(Version);
        module.AddNote("Couchbase uses direct KV for identity operations and parameterized SQL++ for set operations.");
    }
}
