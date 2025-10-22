using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Adapters.Reporting;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Connector.OpenSearch.Discovery;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koan.Data.Connector.OpenSearch.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Connector.OpenSearch";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<OpenSearchOptions>(Infrastructure.Constants.Section);
        services.AddSingleton<IConfigureOptions<OpenSearchOptions>, OpenSearchOptionsConfigurator>();

        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<INamingDefaultsProvider, OpenSearchNamingDefaultsProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, OpenSearchHealthContributor>());

        // Register OpenSearch discovery adapter (maintains "Reference = Intent")
        // Adding Koan.Data.Connector.OpenSearch automatically enables OpenSearch discovery capabilities
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, OpenSearchDiscoveryAdapter>());

        services.AddSingleton<IVectorAdapterFactory, OpenSearchVectorAdapterFactory>();
        services.AddHttpClient(Infrastructure.Constants.HttpClientName);
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from OpenSearchDiscoveryAdapter
        module.AddNote("OpenSearch discovery handled by autonomous OpenSearchDiscoveryAdapter");

        // Configure default options for reporting with provenance metadata
        var defaultOptions = new OpenSearchOptions();

        var connection = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            "ConnectionStrings:OpenSearch");

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

        var connectionIsAuto = string.IsNullOrWhiteSpace(connection.Value) || string.Equals(connection.Value, "auto", StringComparison.OrdinalIgnoreCase);
        var connectionSource = connectionIsAuto ? BootSettingSource.Auto : connection.Source;
        var connectionSourceKey = connection.ResolvedKey ?? Infrastructure.Constants.Configuration.Keys.ConnectionString;

        var effectiveConnectionString = connection.Value ?? defaultOptions.ConnectionString;
        if (connectionIsAuto)
        {
            var adapter = new OpenSearchDiscoveryAdapter(cfg, NullLogger<OpenSearchDiscoveryAdapter>.Instance);
            effectiveConnectionString = AdapterBootReporting.ResolveConnectionString(
                cfg,
                adapter,
                null,
                () => defaultOptions.Endpoint ?? "http://localhost:9200");
        }

        var sanitizedConnection = Redaction.DeIdentify(effectiveConnectionString);

        module.AddSetting(
            "ConnectionString",
            sanitizedConnection,
            source: connectionSource,
            consumers: new[]
            {
                "Koan.Data.Connector.OpenSearch.OpenSearchOptionsConfigurator",
                "Koan.Data.Connector.OpenSearch.OpenSearchVectorAdapterFactory"
            },
            sourceKey: connectionSourceKey);

        module.AddSetting(
            "Endpoint",
            endpoint.Value,
            source: endpoint.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.OpenSearch.OpenSearchOptionsConfigurator",
                "Koan.Data.Connector.OpenSearch.OpenSearchVectorAdapterFactory"
            });

        module.AddSetting(
            "IndexPrefix",
            indexPrefix.Value ?? (defaultOptions.IndexPrefix ?? "koan"),
            source: indexPrefix.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.OpenSearch.OpenSearchVectorAdapterFactory"
            });

        module.AddSetting(
            "VectorField",
            vectorField.Value,
            source: vectorField.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.OpenSearch.OpenSearchVectorAdapterFactory"
            });

        module.AddSetting(
            "MetadataField",
            metadataField.Value,
            source: metadataField.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.OpenSearch.OpenSearchVectorAdapterFactory"
            });

        module.AddSetting(
            "SimilarityMetric",
            similarityMetric.Value,
            source: similarityMetric.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.OpenSearch.OpenSearchVectorAdapterFactory"
            });

        module.AddSetting(
            "TimeoutSeconds",
            timeoutSeconds.Value.ToString(),
            source: timeoutSeconds.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.OpenSearch.OpenSearchVectorAdapterFactory"
            });
    }
}


