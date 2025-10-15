using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.AI.Connector.Ollama.Health;
using Koan.AI.Connector.Ollama.Orchestration;
using Koan.AI.Connector.Ollama.Discovery;
using Koan.AI.Connector.Ollama.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.AI.Connector.Ollama.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.AI.Connector.Ollama";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Use consistent autonomous discovery pattern like all other service adapters
        services.AddKoanOptions<OllamaOptions>(Infrastructure.Constants.Section);
        services.AddSingleton<IConfigureOptions<OllamaOptions>, OllamaOptionsConfigurator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, OllamaDiscoveryAdapter>());

        // Register the hosted service that creates and registers OllamaAdapter instances to the AI registry
        services.AddHostedService<OllamaDiscoveryService>();

        // Register orchestration evaluator for dependency management
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, OllamaOrchestrationEvaluator>());

        // Health reporter so readiness can reflect Ollama availability and models
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, OllamaHealthContributor>());
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from OllamaDiscoveryAdapter
        module.AddNote("Ollama discovery handled by autonomous OllamaDiscoveryAdapter");

        // Configure default options for reporting
        var defaultOptions = new OllamaOptions();

        module.AddSetting("ConnectionString", "auto (resolved by discovery)", isSecret: false);
        module.AddSetting("BaseUrl", defaultOptions.BaseUrl);
        module.AddSetting("DefaultModel", defaultOptions.DefaultModel ?? "none");
        module.AddSetting("AutoDownloadModels", defaultOptions.AutoDownloadModels.ToString());
        module.AddSetting("DefaultPageSize", defaultOptions.DefaultPageSize.ToString());
        module.AddSetting("MaxPageSize", defaultOptions.MaxPageSize.ToString());
    }
}


