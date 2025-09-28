using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Ai.Provider.Ollama.Health;
using Koan.Ai.Provider.Ollama.Orchestration;
using Koan.Ai.Provider.Ollama.Discovery;
using Koan.Ai.Provider.Ollama.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Ai.Provider.Ollama.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Ai.Provider.Ollama";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Use consistent autonomous discovery pattern like all other service adapters
        services.AddKoanOptions<OllamaOptions>(Infrastructure.Constants.Section);
        services.AddSingleton<IConfigureOptions<OllamaOptions>, OllamaOptionsConfigurator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, OllamaDiscoveryAdapter>());

        // Register orchestration evaluator for dependency management
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, OllamaOrchestrationEvaluator>());

        // Health reporter so readiness can reflect Ollama availability and models
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, OllamaHealthContributor>());
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from OllamaDiscoveryAdapter
        report.AddNote("Ollama discovery handled by autonomous OllamaDiscoveryAdapter");

        // Configure default options for reporting
        var defaultOptions = new OllamaOptions();

        report.AddSetting("ConnectionString", "auto (resolved by discovery)", isSecret: false);
        report.AddSetting("BaseUrl", defaultOptions.BaseUrl);
        report.AddSetting("DefaultModel", defaultOptions.DefaultModel ?? "none");
        report.AddSetting("AutoDownloadModels", defaultOptions.AutoDownloadModels.ToString());
        report.AddSetting("DefaultPageSize", defaultOptions.DefaultPageSize.ToString());
        report.AddSetting("MaxPageSize", defaultOptions.MaxPageSize.ToString());
    }
}
