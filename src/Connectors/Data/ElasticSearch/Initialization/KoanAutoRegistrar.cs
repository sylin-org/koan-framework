
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Connector.ElasticSearch.Discovery;

namespace Koan.Data.Connector.ElasticSearch.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Connector.ElasticSearch";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<ElasticSearchOptions>(Infrastructure.Constants.Section);
        services.AddSingleton<IConfigureOptions<ElasticSearchOptions>, ElasticSearchOptionsConfigurator>();

        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<INamingDefaultsProvider, ElasticSearchNamingDefaultsProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, ElasticSearchHealthContributor>());

        // Register ElasticSearch discovery adapter (maintains "Reference = Intent")
        // Adding Koan.Data.Connector.ElasticSearch automatically enables ElasticSearch discovery capabilities
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, ElasticSearchDiscoveryAdapter>());

        services.AddSingleton<IVectorAdapterFactory, ElasticSearchVectorAdapterFactory>();
        services.AddHttpClient(Infrastructure.Constants.HttpClientName);
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from ElasticSearchDiscoveryAdapter
        module.AddNote("ElasticSearch discovery handled by autonomous ElasticSearchDiscoveryAdapter");

        // Configure default options for reporting with provenance metadata
        var defaultOptions = new ElasticSearchOptions();

        var connection = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            "ConnectionStrings:ElasticSearch",
            "ConnectionStrings:Elasticsearch");

        var endpoint = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.Endpoint,
            Infrastructure.Constants.Configuration.Keys.Endpoint,
            Infrastructure.Constants.Configuration.Keys.BaseUrl);

        var indexPrefix = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.IndexPrefix,
            defaultOptions.IndexPrefix ?? "koan");

        var vectorField = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.VectorField,
            defaultOptions.VectorField);

        var metadataField = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.MetadataField,
            defaultOptions.MetadataField);

        var similarityMetric = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.SimilarityMetric,
            defaultOptions.SimilarityMetric);

        var timeoutSeconds = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.TimeoutSeconds,
            defaultOptions.DefaultTimeoutSeconds);

        var connectionValue = string.IsNullOrWhiteSpace(connection.Value)
            ? "auto"
            : connection.Value;
        var connectionIsAuto = string.Equals(connectionValue, "auto", StringComparison.OrdinalIgnoreCase);

        module.AddSetting(
            "ConnectionString",
            connectionIsAuto ? "auto (resolved by discovery)" : connectionValue,
            isSecret: !connectionIsAuto,
            source: connection.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.ElasticSearch.ElasticSearchOptionsConfigurator",
                "Koan.Data.Connector.ElasticSearch.ElasticSearchVectorAdapterFactory"
            },
            sourceKey: connection.ResolvedKey);

        module.AddSetting(
            "Endpoint",
            endpoint.Value,
            source: endpoint.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.ElasticSearch.ElasticSearchOptionsConfigurator",
                "Koan.Data.Connector.ElasticSearch.ElasticSearchVectorAdapterFactory"
            },
            sourceKey: endpoint.ResolvedKey);

        module.AddSetting(
            "IndexPrefix",
            indexPrefix.Value ?? (defaultOptions.IndexPrefix ?? "koan"),
            source: indexPrefix.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.ElasticSearch.ElasticSearchVectorAdapterFactory"
            },
            sourceKey: indexPrefix.ResolvedKey);

        module.AddSetting(
            "VectorField",
            vectorField.Value,
            source: vectorField.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.ElasticSearch.ElasticSearchVectorAdapterFactory"
            },
            sourceKey: vectorField.ResolvedKey);

        module.AddSetting(
            "MetadataField",
            metadataField.Value,
            source: metadataField.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.ElasticSearch.ElasticSearchVectorAdapterFactory"
            },
            sourceKey: metadataField.ResolvedKey);

        module.AddSetting(
            "SimilarityMetric",
            similarityMetric.Value,
            source: similarityMetric.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.ElasticSearch.ElasticSearchVectorAdapterFactory"
            },
            sourceKey: similarityMetric.ResolvedKey);

        module.AddSetting(
            "TimeoutSeconds",
            timeoutSeconds.Value.ToString(),
            source: timeoutSeconds.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.ElasticSearch.ElasticSearchVectorAdapterFactory"
            },
            sourceKey: timeoutSeconds.ResolvedKey);
    }
}


