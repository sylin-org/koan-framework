using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Orchestration.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Orchestration.Connector.Podman.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Orchestration.Connector.Podman";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostingProvider, PodmanProvider>());
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        var provider = new PodmanProvider();
        var engine = provider.EngineInfo();
        var availability = GetAvailability(provider);

        var selected = availability.Ok ? "podman" : "unavailable";
        var candidates = string.Join(", ", new[] { "podman", "docker", "unavailable" });
        module.AddSetting("Engine.Selection", selected);
        module.AddSetting("Engine.Candidates", candidates);
        module.AddSetting("Engine.SelectionReason", availability.Ok ? "Podman CLI reachable" : availability.Reason ?? "No CLI detected");

        module.AddSetting("Engine", engine.Name);
        module.AddSetting("EngineVersion", string.IsNullOrWhiteSpace(engine.Version) ? "unknown" : engine.Version);
        module.AddSetting("Endpoint", string.IsNullOrWhiteSpace(engine.Endpoint) ? "default" : engine.Endpoint);

        var status = availability.Ok ? "reachable" : "unreachable";
        var detail = availability.Reason ?? "no CLI detected";
        module.AddNote($"Podman CLI {status}: {detail}");
    }

    private static (bool Ok, string? Reason) GetAvailability(PodmanProvider provider)
    {
        try
        {
            return provider.IsAvailableAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
