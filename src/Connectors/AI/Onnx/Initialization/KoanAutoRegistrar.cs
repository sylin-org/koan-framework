using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.AI.Contracts.Adapters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.AI.Connector.Onnx.Initialization;

/// <summary>
/// Auto-registers the in-process ONNX embedding contributor (Reference = Intent). It only produces an
/// adapter when a model is configured, so referencing the package is safe even before a model is supplied.
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.AI.Connector.Onnx";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<OnnxOptions>(OnnxOptions.Section);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAiAdapterContributor, OnnxAdapterContributor>());
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        var modelPath = cfg[$"{OnnxOptions.Section}:ModelPath"];
        module.AddSetting("Embeddings", "ONNX Runtime (in-process)");
        module.AddSetting("Model", string.IsNullOrWhiteSpace(modelPath) ? "(not configured)" : modelPath);
        module.AddNote(string.IsNullOrWhiteSpace(modelPath)
            ? "Set Koan:Ai:Onnx:ModelPath to activate in-process embeddings."
            : "In-process embeddings active — no model server required.");
    }
}
