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

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        var exporter = new ComposeExporter();
        module.AddSetting("ExporterId", exporter.Id);
        module.AddSetting("SecretsRefOnly", exporter.Capabilities.SecretsRefOnly.ToString());
        module.AddSetting("ReadinessProbes", exporter.Capabilities.ReadinessProbes.ToString());
        module.AddSetting("TlsHints", exporter.Capabilities.TlsHints.ToString());
    }
}
