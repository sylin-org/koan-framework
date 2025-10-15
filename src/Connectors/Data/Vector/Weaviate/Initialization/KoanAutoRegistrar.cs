using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
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

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from WeaviateDiscoveryAdapter
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
                "Koan.Data.Vector.Connector.Weaviate.WeaviateOptionsConfigurator",
                "Koan.Data.Vector.Connector.Weaviate.WeaviateVectorAdapterFactory"
            });

        module.AddSetting(
            "Endpoint",
            endpoint.Value,
            source: endpoint.Source,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Weaviate.WeaviateOptionsConfigurator",
                "Koan.Data.Vector.Connector.Weaviate.WeaviateVectorAdapterFactory"
            });

        module.AddSetting(
            "DefaultTopK",
            defaultTopK.Value.ToString(),
            source: defaultTopK.Source,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Weaviate.WeaviateVectorAdapterFactory"
            });

        module.AddSetting(
            "MaxTopK",
            maxTopK.Value.ToString(),
            source: maxTopK.Source,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Weaviate.WeaviateVectorAdapterFactory"
            });

        module.AddSetting(
            "Dimension",
            dimension.Value.ToString(),
            source: dimension.Source,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Weaviate.WeaviateVectorAdapterFactory"
            });

        module.AddSetting(
            "Metric",
            metric.Value,
            source: metric.Source,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Weaviate.WeaviateVectorAdapterFactory"
            });

        module.AddSetting(
            "TimeoutSeconds",
            timeoutSeconds.Value.ToString(),
            source: timeoutSeconds.Source,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Weaviate.WeaviateVectorAdapterFactory"
            });
    }

}


