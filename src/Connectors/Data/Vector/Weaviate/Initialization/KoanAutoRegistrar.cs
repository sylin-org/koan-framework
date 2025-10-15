using System;
using System.Globalization;
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
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Connector.Weaviate.Discovery;
using Koan.Data.Vector.Connector.Weaviate.Orchestration;

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
    }

    public void Describe(global::Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
    module.Describe(ModuleVersion);
    module.AddNote("Weaviate discovery handled by autonomous WeaviateDiscoveryAdapter");

        // Configure default options for reporting with provenance metadata
        var defaultOptions = new WeaviateOptions();

        var connection = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            "ConnectionStrings:Weaviate");

        var endpoint = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.Endpoint,
            defaultOptions.Endpoint);

        var defaultTopK = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.DefaultTopK,
            defaultOptions.DefaultTopK);

        var maxTopK = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.MaxTopK,
            defaultOptions.MaxTopK);

        var dimension = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.Dimension,
            defaultOptions.Dimension);

        var metric = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.Metric,
            defaultOptions.Metric);

        var timeoutSeconds = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.TimeoutSeconds,
            defaultOptions.DefaultTimeoutSeconds);

        var connectionIsAuto = string.IsNullOrWhiteSpace(connection.Value) || string.Equals(connection.Value, "auto", StringComparison.OrdinalIgnoreCase);
        var connectionSource = connectionIsAuto ? BootSettingSource.Auto : connection.Source;
        var connectionSourceKey = connectionIsAuto
            ? Infrastructure.Constants.Configuration.Keys.ConnectionString
            : connection.ResolvedKey;

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

        var sanitizedConnection = Redaction.DeIdentify(effectiveConnectionString);

        module.AddSetting(
            "ConnectionString",
            sanitizedConnection,
            source: connectionSource,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Weaviate.WeaviateOptionsConfigurator",
                "Koan.Data.Vector.Connector.Weaviate.WeaviateVectorAdapterFactory"
            },
            sourceKey: connectionSourceKey);

        module.AddSetting(
            "Endpoint",
            endpoint.Value,
            source: endpoint.Source,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Weaviate.WeaviateOptionsConfigurator",
                "Koan.Data.Vector.Connector.Weaviate.WeaviateVectorAdapterFactory"
            },
            sourceKey: endpoint.ResolvedKey);

        module.AddSetting(
            "DefaultTopK",
            defaultTopK.Value.ToString(CultureInfo.InvariantCulture),
            source: defaultTopK.Source,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Weaviate.WeaviateVectorAdapterFactory"
            },
            sourceKey: defaultTopK.ResolvedKey);

        module.AddSetting(
            "MaxTopK",
            maxTopK.Value.ToString(CultureInfo.InvariantCulture),
            source: maxTopK.Source,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Weaviate.WeaviateVectorAdapterFactory"
            },
            sourceKey: maxTopK.ResolvedKey);

        module.AddSetting(
            "Dimension",
            dimension.Value.ToString(CultureInfo.InvariantCulture),
            source: dimension.Source,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Weaviate.WeaviateVectorAdapterFactory"
            },
            sourceKey: dimension.ResolvedKey);

        module.AddSetting(
            "Metric",
            metric.Value,
            source: metric.Source,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Weaviate.WeaviateVectorAdapterFactory"
            },
            sourceKey: metric.ResolvedKey);

        module.AddSetting(
            "TimeoutSeconds",
            timeoutSeconds.Value.ToString(CultureInfo.InvariantCulture),
            source: timeoutSeconds.Source,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Weaviate.WeaviateVectorAdapterFactory"
            },
            sourceKey: timeoutSeconds.ResolvedKey);
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


