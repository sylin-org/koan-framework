using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Core.Configuration;
using Koan.Data.Connector.Sqlite.Discovery;
using Koan.Data.Connector.Sqlite.Infrastructure;
using Koan.Data.Connector.Sqlite.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Sqlite.Initialization;

public sealed class SqliteModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<SqliteOptions>();
        services.AddSingleton<IConfigureOptions<SqliteOptions>, SqliteOptionsConfigurator>();
        services.TryAddSingleton<SqliteConnectionManager>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.AddSingleton<SqliteAdapterFactory>();
        services.AddSingleton<IDataAdapterFactory>(static provider => provider.GetRequiredService<SqliteAdapterFactory>());
        services.AddSingleton<IDataSourceIntegrationFactory>(static provider => provider.GetRequiredService<SqliteAdapterFactory>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, SqliteHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, SqliteDiscoveryAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDataProviderConnectionFactory, SqliteConnectionFactory>());
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        module.Describe(Version);
        module.AddNote("SQLite is a host-owned embedded relational source.");
        var configured = Koan.Core.Configuration.ReadWithSource<string?>(
            configuration,
            Constants.Configuration.Keys.ConnectionString,
            null);
        var isAuto = string.IsNullOrWhiteSpace(configured.Value) ||
                     string.Equals(configured.Value, "auto", StringComparison.OrdinalIgnoreCase);
        var value = isAuto ? "auto" : configured.Value;
        module.PublishConfigValue(
            SqliteProvenanceItems.ConnectionString,
            configured,
            displayOverride: value,
            modeOverride: isAuto
                ? ProvenancePublicationModeExtensions.FromBootSource(BootSettingSource.Auto, usedDefault: true)
                : ProvenancePublicationModeExtensions.FromConfigurationValue(configured),
            usedDefaultOverride: isAuto,
            sourceKeyOverride: configured.ResolvedKey ?? Constants.Configuration.Keys.ConnectionString,
            sanitizeOverride: !isAuto);
    }
}
