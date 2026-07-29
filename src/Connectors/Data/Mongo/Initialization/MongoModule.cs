using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Mongo.Discovery;
using Koan.Data.Connector.Mongo.Infrastructure;
using Koan.Data.Connector.Mongo.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Mongo.Initialization;

public sealed class MongoModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<MongoOptions>();
        services.AddSingleton<IConfigureOptions<MongoOptions>, MongoOptionsConfigurator>();
        services.TryAddSingleton<MongoClientManager>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.AddSingleton<MongoAdapterFactory>();
        services.AddSingleton<IDataAdapterFactory>(static provider => provider.GetRequiredService<MongoAdapterFactory>());
        services.AddSingleton<IDataSourceIntegrationFactory>(static provider => provider.GetRequiredService<MongoAdapterFactory>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, MongoHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, MongoDiscoveryAdapter>());
    }

    public override void Report(
        ProvenanceModuleWriter module,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        module.Describe(Version);
        module.AddNote("MongoDB is a host-owned native document source.");
        var connection = Koan.Core.Configuration.ReadWithSource<string?>(
            configuration,
            Constants.Configuration.ConnectionString,
            null);
        var database = Koan.Core.Configuration.ReadWithSource<string?>(
            configuration,
            Constants.Configuration.Database,
            new MongoOptions().Database);
        module.PublishConfigValue(
            MongoProvenanceItems.ConnectionString,
            connection,
            displayOverride: connection.Value ?? "auto",
            modeOverride: string.IsNullOrWhiteSpace(connection.Value) ||
                          string.Equals(connection.Value, "auto", StringComparison.OrdinalIgnoreCase)
                ? ProvenancePublicationModeExtensions.FromBootSource(BootSettingSource.Auto, usedDefault: true)
                : ProvenancePublicationModeExtensions.FromConfigurationValue(connection),
            sanitizeOverride: true);
        module.PublishConfigValue(MongoProvenanceItems.Database, database, database.Value ?? new MongoOptions().Database);
    }
}
