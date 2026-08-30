using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Core.Configuration;
using Koan.Data.Connector.SqlServer.Discovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Connector.SqlServer.Initialization;

public sealed class SqlServerModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<SqlServerOptions>();
        services.AddSingleton<IConfigureOptions<SqlServerOptions>, SqlServerOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.AddSingleton<SqlServerAdapterFactory>();
        services.AddSingleton<IDataAdapterFactory>(static provider => provider.GetRequiredService<SqlServerAdapterFactory>());
        services.AddSingleton<IDataSourceIntegrationFactory>(static provider => provider.GetRequiredService<SqlServerAdapterFactory>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, SqlServerHealthContributor>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDataFailureClassifier, Runtime.SqlServerFailureClassifier>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, SqlServerDiscoveryAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDataProviderConnectionFactory, SqlServerConnectionFactory>());
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        module.Describe(Version);
        module.AddNote("SQL Server provides one relational execution path for managed and externally mapped sources.");
    }
}
