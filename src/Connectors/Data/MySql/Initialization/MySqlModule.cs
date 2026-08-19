using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Core.Configuration;
using Koan.Data.Connector.MySql.Discovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.MySql.Initialization;

public sealed class MySqlModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<MySqlOptions>(Infrastructure.Constants.Configuration.Section);
        services.AddSingleton<IConfigureOptions<MySqlOptions>, MySqlOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.AddSingleton<MySqlAdapterFactory>();
        services.AddSingleton<IDataAdapterFactory>(static provider => provider.GetRequiredService<MySqlAdapterFactory>());
        services.AddSingleton<IDataSourceIntegrationFactory>(static provider => provider.GetRequiredService<MySqlAdapterFactory>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, MySqlHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, MySqlDiscoveryAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDataProviderConnectionFactory, MySqlConnectionFactory>());
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        module.Describe(Version);
        module.AddNote("MySQL provides one relational execution path for managed and externally mapped sources.");
    }
}
