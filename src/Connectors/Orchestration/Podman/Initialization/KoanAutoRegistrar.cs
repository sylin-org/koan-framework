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

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        var provider = new PodmanProvider();
        var engine = provider.EngineInfo();
        var availability = GetAvailability(provider);

        var selected = availability.Ok ? "podman" : "unavailable";
        report.AddProviderElection(
            "HostingEngine",
            selected,
            new[] { "podman", "docker", "unavailable" },
            availability.Ok ? "Podman CLI reachable" : availability.Reason ?? "No CLI detected");

        report.AddSetting("Engine", engine.Name);
        report.AddSetting("EngineVersion", string.IsNullOrWhiteSpace(engine.Version) ? "unknown" : engine.Version);
        report.AddSetting("Endpoint", string.IsNullOrWhiteSpace(engine.Endpoint) ? "default" : engine.Endpoint);

        report.AddConnectionAttempt(
            "HostingEngine.Podman",
            "podman version",
            availability.Ok,
            availability.Reason);
    }

    private static (bool Ok, string? Reason) GetAvailability(PodmanProvider provider)
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