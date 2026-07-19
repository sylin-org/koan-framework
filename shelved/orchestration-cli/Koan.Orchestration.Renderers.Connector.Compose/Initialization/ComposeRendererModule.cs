using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Orchestration.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Orchestration.Renderers.Connector.Compose.Initialization;

public sealed class ComposeRendererModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IArtifactExporter, ComposeExporter>());
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        var exporter = new ComposeExporter();
        module.AddSetting("ExporterId", exporter.Id);
        module.AddSetting("SecretsRefOnly", exporter.Capabilities.SecretsRefOnly.ToString());
        module.AddSetting("ReadinessProbes", exporter.Capabilities.ReadinessProbes.ToString());
        module.AddSetting("TlsHints", exporter.Capabilities.TlsHints.ToString());
    }
}
