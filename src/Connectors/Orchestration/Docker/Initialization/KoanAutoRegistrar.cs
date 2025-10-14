using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Orchestration.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Orchestration.Connector.Docker.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Orchestration.Connector.Docker";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Ensure the Docker hosting provider is available via DI once this package is referenced.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostingProvider, DockerProvider>());
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        var provider = new DockerProvider();
        var engine = provider.EngineInfo();
        var availability = GetAvailability(provider);

        var selected = availability.Ok ? "docker" : "unavailable";
        var candidates = string.Join(", ", new[] { "docker", "podman", "unavailable" });
        report.AddSetting("Engine.Selection", selected);
        report.AddSetting("Engine.Candidates", candidates);
        report.AddSetting("Engine.SelectionReason", availability.Ok ? "Docker CLI reachable" : availability.Reason ?? "No CLI detected");

        report.AddSetting("Engine", engine.Name);
        report.AddSetting("EngineVersion", string.IsNullOrWhiteSpace(engine.Version) ? "unknown" : engine.Version);
        report.AddSetting("Context", string.IsNullOrWhiteSpace(engine.Endpoint) ? "default" : engine.Endpoint);

        var status = availability.Ok ? "reachable" : "unreachable";
        var detail = availability.Reason ?? "no CLI detected";
        report.AddNote($"Docker CLI {status}: {detail}");
    }

    private static (bool Ok, string? Reason) GetAvailability(DockerProvider provider)
    {
        try
        {
            return provider.IsAvailableAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}