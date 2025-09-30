using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Microsoft.Extensions.Logging;
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
        var logger = services.BuildServiceProvider().GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Koan.Data.Vector.Connector.Weaviate.Initialization.KoanAutoRegistrar");
        logger?.Log(LogLevel.Debug, "Koan.Data.Vector.Connector.Weaviate KoanAutoRegistrar loaded.");
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

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from WeaviateDiscoveryAdapter
        report.AddNote("Weaviate discovery handled by autonomous WeaviateDiscoveryAdapter");

        // Configure default options for reporting
        var defaultOptions = new WeaviateOptions();

        report.AddSetting("ConnectionString", "auto (resolved by discovery)", isSecret: false);
        report.AddSetting("Endpoint", "auto (resolved by discovery)", isSecret: false);
        report.AddSetting("DefaultTopK", defaultOptions.DefaultTopK.ToString());
        report.AddSetting("MaxTopK", defaultOptions.MaxTopK.ToString());
        report.AddSetting("Dimension", defaultOptions.Dimension.ToString());
        report.AddSetting("Metric", defaultOptions.Metric);
        report.AddSetting("TimeoutSeconds", defaultOptions.DefaultTimeoutSeconds.ToString());
    }

}

