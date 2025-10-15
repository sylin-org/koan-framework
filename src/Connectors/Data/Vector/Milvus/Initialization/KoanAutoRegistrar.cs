
using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Koan.Core;
using Koan.Core.Adapters.Reporting;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Connector.Milvus.Discovery;

namespace Koan.Data.Vector.Connector.Milvus.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Vector.Connector.Milvus";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<MilvusOptions>(Infrastructure.Constants.Section);
        services.AddSingleton<IConfigureOptions<MilvusOptions>, MilvusOptionsConfigurator>();

        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<INamingDefaultsProvider, MilvusNamingDefaultsProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, MilvusHealthContributor>());

        // Register Milvus discovery adapter (maintains "Reference = Intent")
        // Adding Koan.Data.Vector.Connector.Milvus automatically enables Milvus discovery capabilities
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, MilvusDiscoveryAdapter>());

        services.AddSingleton<IVectorAdapterFactory, MilvusVectorAdapterFactory>();
        services.AddHttpClient(Infrastructure.Constants.HttpClientName);
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from MilvusDiscoveryAdapter
        module.AddNote("Milvus discovery handled by autonomous MilvusDiscoveryAdapter");

        // Configure default options for reporting with provenance metadata
        var defaultOptions = new MilvusOptions();

        var connection = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            "ConnectionStrings:Milvus");

        var endpoint = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.Endpoint,
            defaultOptions.Endpoint);

        var databaseName = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.DatabaseName,
            defaultOptions.DatabaseName);

        var vectorField = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.VectorFieldName,
            defaultOptions.VectorFieldName);

        var metadataField = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.MetadataFieldName,
            defaultOptions.MetadataFieldName);

        var metric = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.Metric,
            defaultOptions.Metric);

        var consistencyLevel = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.ConsistencyLevel,
            defaultOptions.ConsistencyLevel);

        var timeoutSeconds = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.TimeoutSeconds,
            defaultOptions.DefaultTimeoutSeconds);

        var autoCreate = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.AutoCreateCollection,
            defaultOptions.AutoCreateCollection);

        var connectionIsAuto = string.IsNullOrWhiteSpace(connection.Value) || string.Equals(connection.Value, "auto", StringComparison.OrdinalIgnoreCase);
        var connectionSource = connectionIsAuto ? BootSettingSource.Auto : connection.Source;
        var connectionSourceKey = connection.ResolvedKey ?? Infrastructure.Constants.Configuration.Keys.ConnectionString;

        var effectiveConnectionString = connection.Value ?? defaultOptions.ConnectionString;
        if (connectionIsAuto)
        {
            var adapter = new MilvusDiscoveryAdapter(cfg, NullLogger<MilvusDiscoveryAdapter>.Instance);
            effectiveConnectionString = AdapterBootReporting.ResolveConnectionString(
                cfg,
                adapter,
                null,
                () => BuildMilvusFallback(defaultOptions, endpoint.Value));
        }

        var sanitizedConnection = Redaction.DeIdentify(effectiveConnectionString);

        module.AddSetting(
            "ConnectionString",
            sanitizedConnection,
            source: connectionSource,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Milvus.MilvusOptionsConfigurator",
                "Koan.Data.Vector.Connector.Milvus.MilvusVectorAdapterFactory"
            },
            sourceKey: connectionSourceKey);

        module.AddSetting(
            "Endpoint",
            endpoint.Value,
            source: endpoint.Source,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Milvus.MilvusOptionsConfigurator",
                "Koan.Data.Vector.Connector.Milvus.MilvusVectorAdapterFactory"
            });

        module.AddSetting(
            "DatabaseName",
            databaseName.Value,
            source: databaseName.Source,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Milvus.MilvusVectorAdapterFactory"
            });

        module.AddSetting(
            "VectorFieldName",
            vectorField.Value,
            source: vectorField.Source,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Milvus.MilvusVectorAdapterFactory"
            });

        module.AddSetting(
            "MetadataFieldName",
            metadataField.Value,
            source: metadataField.Source,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Milvus.MilvusVectorAdapterFactory"
            });

        module.AddSetting(
            "Metric",
            metric.Value,
            source: metric.Source,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Milvus.MilvusVectorAdapterFactory"
            });

        module.AddSetting(
            "ConsistencyLevel",
            consistencyLevel.Value,
            source: consistencyLevel.Source,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Milvus.MilvusVectorAdapterFactory"
            });

        module.AddSetting(
            "TimeoutSeconds",
            timeoutSeconds.Value.ToString(),
            source: timeoutSeconds.Source,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Milvus.MilvusVectorAdapterFactory"
            });

        module.AddSetting(
            "AutoCreateCollection",
            autoCreate.Value ? "true" : "false",
            source: autoCreate.Source,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Milvus.MilvusVectorAdapterFactory"
            });
    }

    private static string BuildMilvusFallback(MilvusOptions defaults, string? configuredEndpoint)
    {
        var endpoint = !string.IsNullOrWhiteSpace(configuredEndpoint)
            ? configuredEndpoint
            : defaults.Endpoint ?? "http://localhost:19530";

        return NormalizeMilvusEndpoint(endpoint);
    }

    private static string NormalizeMilvusEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return "milvus://localhost:19530";
        }

        if (endpoint.StartsWith("milvus://", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            var port = uri.IsDefaultPort ? 19530 : uri.Port;
            return $"milvus://{uri.Host}:{port}";
        }

        return endpoint;
    }
}


