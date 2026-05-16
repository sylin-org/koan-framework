using Koan.Cache.Coherence.InMemory.Extensions;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Cache.Coherence.InMemory.Initialization;

/// <summary>
/// Reference = Intent: referencing <c>Koan.Cache.Coherence.InMemory</c> auto-registers the
/// in-process coherence channel, making the cache coordinator activate in
/// <c>CoherenceMode.AutoDetect</c>.
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Cache.Coherence.InMemory";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanCacheInMemoryCoherence();
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        module.AddSetting(
            "Transport",
            "in-memory",
            source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Auto,
            consumers: new[] { "Koan.Cache.Coherence.CoordinatorChannel" });
    }
}
