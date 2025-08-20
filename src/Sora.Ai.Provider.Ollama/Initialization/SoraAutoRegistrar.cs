using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Sora.Core;

namespace Sora.Ai.Provider.Ollama.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Ai.Provider.Ollama";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        // Enable config-based registration; discovery will be added later.
        services.AddOllamaFromConfig();
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var nodes = cfg.GetSection(Sora.Ai.Provider.Ollama.Infrastructure.Constants.Configuration.ServicesRoot).GetChildren().ToList();
        if (nodes.Count == 0)
            report.AddNote("No explicit Ollama services configured (Dev auto-discovery may register).");
        else
            report.AddNote($"Configured Ollama services: {string.Join(", ", nodes.Select(n => n["Id"]))}");
    }
}
