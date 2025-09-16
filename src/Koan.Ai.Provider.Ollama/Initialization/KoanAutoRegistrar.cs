using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Ai.Provider.Ollama.Health;
using Koan.Core;

namespace Koan.Ai.Provider.Ollama.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Ai.Provider.Ollama";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Enable options and hosted services for config registration and discovery
        services.AddOllamaFromConfig();
        services.AddSingleton<IHostedService, OllamaConfigRegistrationService>();
        services.AddSingleton<IHostedService, OllamaDiscoveryService>();
        // Health reporter so readiness can reflect Ollama availability and models
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, OllamaHealthContributor>());
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var nodes = cfg.GetSection(Infrastructure.Constants.Configuration.ServicesRoot).GetChildren().ToList();
        if (nodes.Count == 0)
            report.AddNote("No explicit Ollama services configured (Dev auto-discovery may register).");
        else
            report.AddNote($"Configured Ollama services: {string.Join(", ", nodes.Select(n => n["Id"]))}");
        // Discovery visibility
        report.AddSetting("Discovery:EnvBaseUrl", Infrastructure.Constants.Discovery.EnvBaseUrl, isSecret: false);
        report.AddSetting("Discovery:EnvList", Infrastructure.Constants.Discovery.EnvList, isSecret: false);
        report.AddSetting("Discovery:DefaultPort", Infrastructure.Constants.Discovery.DefaultPort.ToString(), isSecret: false);
    }
}
