using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Microsoft.Extensions.Logging;
using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Weaviate.Orchestration;

namespace Koan.Data.Weaviate.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Weaviate";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        var logger = services.BuildServiceProvider().GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Koan.Data.Weaviate.Initialization.KoanAutoRegistrar");
        logger?.Log(LogLevel.Debug, "Koan.Data.Weaviate KoanAutoRegistrar loaded.");
        services.AddKoanOptions<WeaviateOptions>(Infrastructure.Constants.Configuration.Section);

        // Register orchestration-aware configuration
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<WeaviateOptions>, WeaviateOptionsConfigurator>());

        services.TryAddSingleton<Abstractions.Naming.IStorageNameResolver, Abstractions.Naming.DefaultStorageNameResolver>();
        services.TryAddEnumerable(new ServiceDescriptor(typeof(Abstractions.Naming.INamingDefaultsProvider), typeof(WeaviateNamingDefaultsProvider), ServiceLifetime.Singleton));
        services.AddSingleton<IVectorAdapterFactory, WeaviateVectorAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, WeaviateHealthContributor>());

        // Register orchestration evaluator for dependency management
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, WeaviateOrchestrationEvaluator>());

        services.AddHttpClient("weaviate");
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var endpoint = Configuration.Read(cfg, "Koan:Data:Weaviate:Endpoint", null) ?? "http://localhost:8080";
        report.AddSetting("Weaviate:Endpoint", endpoint, isSecret: false);
        report.AddSetting("Weaviate:OrchestrationMode", KoanEnv.OrchestrationMode.ToString(), isSecret: false);
        report.AddSetting("Weaviate:Configuration", "Orchestration-aware service discovery enabled", isSecret: false);
    }

}
