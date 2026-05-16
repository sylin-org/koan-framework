using Koan.Cache.Abstractions;
using Koan.Cache.Extensions;
using Koan.Core;
using Koan.Core.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Cache.Adapters.Memory;

public sealed class MemoryAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Cache.Adapter.Memory";
    public string? ModuleVersion => typeof(MemoryAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanCacheAdapter("memory");
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        var capacity = Configuration.Read(cfg, CacheConstants.Configuration.Memory.TagIndexCapacity, 2048);

        module.AddSetting("CacheStore.Selected", "memory");
        module.AddSetting("CacheStore.Candidates", "memory, redis, custom");
        module.AddSetting("CacheStore.Rationale", "Reference = memory adapter package");
        module.AddSetting("TagIndexCapacity", capacity.ToString());
        // SWR is per-call opt-in via CacheReadOptions.AllowStaleFor (ARCH-0078).
    }
}
