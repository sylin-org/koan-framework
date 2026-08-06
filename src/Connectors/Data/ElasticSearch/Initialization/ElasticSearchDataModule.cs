using System.Globalization;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.ElasticSearch.Discovery;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.ElasticSearch.Initialization;

public sealed class ElasticSearchDataModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<ElasticSearchOptions>(Infrastructure.Constants.Configuration.Section);
        services.AddSingleton<IConfigureOptions<ElasticSearchOptions>, ElasticSearchOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, ElasticSearchHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, ElasticSearchDiscoveryAdapter>());
        services.AddSingleton<ElasticSearchVectorAdapterFactory>();
        services.AddSingleton<IVectorAdapterFactory>(static provider =>
            provider.GetRequiredService<ElasticSearchVectorAdapterFactory>());
        services.AddHttpClient(Infrastructure.Constants.HttpClientName);
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        module.Describe(Version);
        var defaults = new ElasticSearchOptions();
        var endpoint = Configuration.ReadFirstWithSource(
            configuration,
            defaults.Endpoint,
            Infrastructure.Constants.Configuration.Keys.Endpoint,
            Infrastructure.Constants.Configuration.Keys.LegacyConnectionString,
            "ConnectionStrings:ElasticSearch");
        var timeout = Configuration.ReadWithSource(
            configuration,
            Infrastructure.Constants.Configuration.Keys.TimeoutSeconds,
            defaults.TimeoutSeconds);
        module.AddSetting("Endpoint", Redaction.DeIdentify(endpoint.Value), source: endpoint.Source,
            consumers: [typeof(ElasticSearchVectorAdapterFactory).FullName!], sourceKey: endpoint.ResolvedKey);
        module.AddSetting("TimeoutSeconds", timeout.Value.ToString(CultureInfo.InvariantCulture), source: timeout.Source,
            consumers: [typeof(ElasticSearchClient).FullName!], sourceKey: timeout.ResolvedKey);
        module.AddNote(
            "Vector shape and visibility come from the immutable Koan VectorSpacePlan; Elasticsearch options own placement and bounds only.");
    }
}
