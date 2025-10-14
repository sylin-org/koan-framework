
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
using Koan.Data.Connector.OpenSearch.Discovery;

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

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from OpenSearchDiscoveryAdapter
        report.AddNote("OpenSearch discovery handled by autonomous OpenSearchDiscoveryAdapter");

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

        var connectionValue = string.IsNullOrWhiteSpace(connection.Value)
            ? "auto"
            : connection.Value;
        var connectionIsAuto = string.Equals(connectionValue, "auto", StringComparison.OrdinalIgnoreCase);

        report.AddSetting(
            "ConnectionString",
            connectionIsAuto ? "auto (resolved by discovery)" : connectionValue,
            isSecret: !connectionIsAuto,
            source: connection.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.OpenSearch.OpenSearchOptionsConfigurator",
                "Koan.Data.Connector.OpenSearch.OpenSearchVectorAdapterFactory"
            });

        report.AddSetting(
            "Endpoint",
            endpoint.Value,
            source: endpoint.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.OpenSearch.OpenSearchOptionsConfigurator",
                "Koan.Data.Connector.OpenSearch.OpenSearchVectorAdapterFactory"
            });

        report.AddSetting(
            "IndexPrefix",
            indexPrefix.Value ?? (defaultOptions.IndexPrefix ?? "koan"),
            source: indexPrefix.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.OpenSearch.OpenSearchVectorAdapterFactory"
            });

        report.AddSetting(
            "VectorField",
            vectorField.Value,
            source: vectorField.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.OpenSearch.OpenSearchVectorAdapterFactory"
            });

        report.AddSetting(
            "MetadataField",
            metadataField.Value,
            source: metadataField.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.OpenSearch.OpenSearchVectorAdapterFactory"
            });

        report.AddSetting(
            "SimilarityMetric",
            similarityMetric.Value,
            source: similarityMetric.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.OpenSearch.OpenSearchVectorAdapterFactory"
            });

        report.AddSetting(
            "TimeoutSeconds",
            timeoutSeconds.Value.ToString(),
            source: timeoutSeconds.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.OpenSearch.OpenSearchVectorAdapterFactory"
            });
    }
}

