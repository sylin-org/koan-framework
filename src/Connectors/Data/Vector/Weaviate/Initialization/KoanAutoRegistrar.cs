using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Adapters.Reporting;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Connector.Weaviate.Discovery;
using Koan.Data.Vector.Connector.Weaviate.Orchestration;
using WeaviateItems = Koan.Data.Vector.Connector.Weaviate.Infrastructure.WeaviateProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Data.Vector.Connector.Weaviate.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Vector.Connector.Weaviate";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<WeaviateOptions>(Infrastructure.Constants.Configuration.Section);

        services.AddSingleton<IConfigureOptions<WeaviateOptions>, WeaviateOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(new ServiceDescriptor(typeof(INamingDefaultsProvider), typeof(WeaviateNamingDefaultsProvider), ServiceLifetime.Singleton));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, WeaviateHealthContributor>());

        // Register orchestration evaluator for dependency management
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, WeaviateOrchestrationEvaluator>());

        // Register Weaviate discovery adapter (maintains "Reference = Intent")
        // Adding Koan.Data.Vector.Connector.Weaviate automatically enables Weaviate discovery capabilities
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, WeaviateDiscoveryAdapter>());

        services.AddSingleton<IVectorAdapterFactory, WeaviateVectorAdapterFactory>();
        services.AddHttpClient("weaviate");

        // Register partition mapper for per-partition class strategy (ARCH-0071, uses EntityContext from DATA-0077)
        services.TryAddSingleton<Koan.Data.Vector.Abstractions.Partition.IVectorPartitionMapper, Koan.Data.Vector.Connector.Weaviate.Partition.WeaviatePartitionMapper>();
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        module.AddNote("Weaviate discovery handled by autonomous WeaviateDiscoveryAdapter");

        // Configure default options for reporting with provenance metadata
        var defaultOptions = new WeaviateOptions();

        var connection = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.ConnectionString,
            WeaviateItems.ConnectionStringKeys);

        var endpoint = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.Endpoint,
            WeaviateItems.EndpointKeys);

        var defaultTopK = Configuration.ReadWithSource(
            cfg,
            WeaviateItems.DefaultTopK.Key,
            defaultOptions.DefaultTopK);

        var maxTopK = Configuration.ReadWithSource(
            cfg,
            WeaviateItems.MaxTopK.Key,
            defaultOptions.MaxTopK);

        var dimension = Configuration.ReadWithSource(
            cfg,
            WeaviateItems.Dimension.Key,
            defaultOptions.Dimension);

        var metric = Configuration.ReadWithSource(
            cfg,
            WeaviateItems.Metric.Key,
            defaultOptions.Metric);

        var timeoutSeconds = Configuration.ReadWithSource(
            cfg,
            WeaviateItems.TimeoutSeconds.Key,
            defaultOptions.DefaultTimeoutSeconds);

        var connectionIsAuto = string.IsNullOrWhiteSpace(connection.Value) || string.Equals(connection.Value, "auto", StringComparison.OrdinalIgnoreCase);
        var connectionMode = connectionIsAuto
            ? ProvenanceModes.FromBootSource(BootSettingSource.Auto, usedDefault: true)
            : ProvenanceModes.FromConfigurationValue(connection);
        var connectionSourceKey = connection.ResolvedKey ?? WeaviateItems.ConnectionString.Key;

        var effectiveConnectionString = connection.Value ?? defaultOptions.ConnectionString;
        if (connectionIsAuto)
        {
            var adapter = new WeaviateDiscoveryAdapter(cfg, NullLogger<WeaviateDiscoveryAdapter>.Instance);
            effectiveConnectionString = AdapterBootReporting.ResolveConnectionString(
                cfg,
                adapter,
                null,
                () => BuildWeaviateFallback(defaultOptions, endpoint.Value));
        }

        module.AddSetting(
            WeaviateItems.ConnectionString,
            connectionMode,
            effectiveConnectionString,
            sourceKey: connectionSourceKey,
            usedDefault: connectionIsAuto ? true : connection.UsedDefault);

        module.AddSetting(
            WeaviateItems.Endpoint,
            ProvenanceModes.FromConfigurationValue(endpoint),
            endpoint.Value,
            sourceKey: endpoint.ResolvedKey,
            usedDefault: endpoint.UsedDefault);

        module.AddSetting(
            WeaviateItems.DefaultTopK,
            ProvenanceModes.FromConfigurationValue(defaultTopK),
            defaultTopK.Value,
            sourceKey: defaultTopK.ResolvedKey,
            usedDefault: defaultTopK.UsedDefault);

        module.AddSetting(
            WeaviateItems.MaxTopK,
            ProvenanceModes.FromConfigurationValue(maxTopK),
            maxTopK.Value,
            sourceKey: maxTopK.ResolvedKey,
            usedDefault: maxTopK.UsedDefault);

        module.AddSetting(
            WeaviateItems.Dimension,
            ProvenanceModes.FromConfigurationValue(dimension),
            dimension.Value,
            sourceKey: dimension.ResolvedKey,
            usedDefault: dimension.UsedDefault);

        module.AddSetting(
            WeaviateItems.Metric,
            ProvenanceModes.FromConfigurationValue(metric),
            metric.Value,
            sourceKey: metric.ResolvedKey,
            usedDefault: metric.UsedDefault);

        module.AddSetting(
            WeaviateItems.TimeoutSeconds,
            ProvenanceModes.FromConfigurationValue(timeoutSeconds),
            timeoutSeconds.Value,
            sourceKey: timeoutSeconds.ResolvedKey,
            usedDefault: timeoutSeconds.UsedDefault);
    }
    private static string BuildWeaviateFallback(WeaviateOptions defaults, string? configuredEndpoint)
    {
        var endpoint = !string.IsNullOrWhiteSpace(configuredEndpoint)
            ? configuredEndpoint
            : defaults.Endpoint;

        return NormalizeWeaviateEndpoint(endpoint);
    }

    private static string NormalizeWeaviateEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return "http://localhost:8085";
        }

        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            var scheme = string.IsNullOrWhiteSpace(uri.Scheme) ? "http" : uri.Scheme;
            var portSegment = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
            return $"{scheme}://{uri.Host}{portSegment}";
        }

        return endpoint;
    }
}


