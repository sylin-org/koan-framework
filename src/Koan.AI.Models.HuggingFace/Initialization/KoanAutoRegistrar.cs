using Koan.AI.Models;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.AI.Models.HuggingFace.Initialization;

/// <summary>
/// Auto-registers the HuggingFace Hub model source when this package is referenced.
/// Follows the Reference = Intent pattern.
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.AI.Models.HuggingFace";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<HuggingFaceOptions>("Koan:Ai:HuggingFace");
        services.TryAddSingleton<HuggingFaceClient>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IModelSourceProvider, HuggingFaceModelSourceProvider>());
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);

        var section = cfg.GetSection("Koan:Ai:HuggingFace");
        var hasToken = !string.IsNullOrWhiteSpace(section["Token"])
                       || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HF_TOKEN"));

        module.SetNote("auth", n => n.Message(hasToken
            ? "HuggingFace Hub connected (authenticated — private/gated models available)."
            : "HuggingFace Hub connected (anonymous — public models only)."));

        var baseUrl = section["BaseUrl"];

        if (!string.IsNullOrWhiteSpace(baseUrl) && baseUrl != "https://huggingface.co")
        {
            module.SetNote("endpoint", n => n.Message($"Custom Hub URL: {baseUrl}"));
        }
    }
}
