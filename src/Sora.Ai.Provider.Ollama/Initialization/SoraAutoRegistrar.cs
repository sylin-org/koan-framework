using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Ai.Provider.Ollama.Health;

namespace Sora.Ai.Provider.Ollama.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Ai.Provider.Ollama";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        // Enable options and hosted services for config registration and discovery
        services.AddOllamaFromConfig();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService, OllamaConfigRegistrationService>();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService, OllamaDiscoveryService>();
        // Health reporter so readiness can reflect Ollama availability and models
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Sora.Core.IHealthContributor, OllamaHealthContributor>());
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var nodes = cfg.GetSection(Sora.Ai.Provider.Ollama.Infrastructure.Constants.Configuration.ServicesRoot).GetChildren().ToList();
        if (nodes.Count == 0)
            report.AddNote("No explicit Ollama services configured (Dev auto-discovery may register).");
        else
            report.AddNote($"Configured Ollama services: {string.Join(", ", nodes.Select(n => n["Id"]))}");
    // Discovery visibility
    report.AddSetting("Discovery:EnvBaseUrl", Sora.Ai.Provider.Ollama.Infrastructure.Constants.Discovery.EnvBaseUrl, isSecret: false);
    report.AddSetting("Discovery:EnvList", Sora.Ai.Provider.Ollama.Infrastructure.Constants.Discovery.EnvList, isSecret: false);
    report.AddSetting("Discovery:DefaultPort", Sora.Ai.Provider.Ollama.Infrastructure.Constants.Discovery.DefaultPort.ToString(), isSecret: false);
    }
}
