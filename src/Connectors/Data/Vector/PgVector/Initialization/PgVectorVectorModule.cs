using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Connector.PgVector.Discovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Data.Vector.Connector.PgVector.Initialization;

/// <summary>Registers pgvector by package reference.</summary>
public sealed class PgVectorVectorModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<PgVectorOptions>(PgVectorOptions.Section);
        services.AddSingleton<IConfigureOptions<PgVectorOptions>, PgVectorOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, PgVectorHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, PgVectorDiscoveryAdapter>());
        services.AddSingleton<PgVectorVectorAdapterFactory>();
        services.AddSingleton<IVectorAdapterFactory>(static provider =>
            provider.GetRequiredService<PgVectorVectorAdapterFactory>());
    }

    public override void Report(
        ProvenanceModuleWriter module,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        module.Describe(Version);
        var route = PgVectorRoute.ResolveDefault(configuration);
        module.AddSetting("Store", Redaction.DeIdentify(route.ConnectionString));
        module.AddSetting("Placement", route.Origin);
        module.AddNote("PostgreSQL exact vector search; shape comes from VectorSpacePlan and metadata predicates execute in JSONB.");
    }
}
