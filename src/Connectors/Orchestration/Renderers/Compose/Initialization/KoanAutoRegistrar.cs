using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Orchestration.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Orchestration.Renderers.Connector.Compose.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Orchestration.Renderers.Connector.Compose";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IArtifactExporter, ComposeExporter>());
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        var exporter = new ComposeExporter();
        report.AddSetting("ExporterId", exporter.Id);
    report.AddSetting("SecretsRefOnly", exporter.Capabilities.SecretsRefOnly.ToString());
    report.AddSetting("ReadinessProbes", exporter.Capabilities.ReadinessProbes.ToString());
    report.AddSetting("TlsHints", exporter.Capabilities.TlsHints.ToString());
    }
}