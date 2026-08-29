using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Firebird.Discovery;
using Koan.Data.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Firebird.Initialization;

public sealed class FirebirdModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<FirebirdOptions>(Infrastructure.Constants.Configuration.Section);
        services.AddSingleton<IConfigureOptions<FirebirdOptions>, FirebirdOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.AddSingleton<FirebirdAdapterFactory>();
        services.AddSingleton<IDataAdapterFactory>(static provider => provider.GetRequiredService<FirebirdAdapterFactory>());
        services.AddSingleton<IDataSourceIntegrationFactory>(static provider => provider.GetRequiredService<FirebirdAdapterFactory>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, FirebirdHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, FirebirdDiscoveryAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDataProviderConnectionFactory, FirebirdConnectionFactory>());
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        module.Describe(Version);
        module.AddNote("Firebird provides one relational execution path for managed and externally mapped sources.");
    }
}
