using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Vector.Connector.MongoAtlasVector.Initialization;

/// <summary>Registers Atlas Vector Search by package reference.</summary>
public sealed class MongoAtlasVectorModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<MongoAtlasVectorOptions>(MongoAtlasVectorOptions.Section);
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddSingleton<MongoAtlasVectorClientManager>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, MongoAtlasVectorHealthContributor>());
        services.AddSingleton<MongoAtlasVectorAdapterFactory>();
        services.AddSingleton<IVectorAdapterFactory>(static provider =>
            provider.GetRequiredService<MongoAtlasVectorAdapterFactory>());
    }

    public override void Report(
        ProvenanceModuleWriter module,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        module.Describe(Version);
        var endpoint = configuration[Infrastructure.Constants.Configuration.Keys.ConnectionString]
            ?? configuration.GetConnectionString("MongoAtlasVector")
            ?? configuration[Infrastructure.Constants.Configuration.PairedConnectionString]
            ?? configuration.GetConnectionString("Mongo")
            ?? Infrastructure.Constants.Configuration.Automatic;
        var database = configuration[Infrastructure.Constants.Configuration.Keys.Database]
            ?? Infrastructure.Constants.Defaults.Database;
        module.AddSetting("Store", Redaction.DeIdentify(endpoint));
        module.AddSetting("Database", database);
        module.AddNote("Exact Atlas Search vectors on the existing Mongo endpoint; vector storage remains physically separate from record collections.");
    }
}
