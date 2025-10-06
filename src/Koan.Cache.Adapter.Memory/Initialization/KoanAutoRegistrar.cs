using Koan.Cache.Abstractions;
using Koan.Cache.Extensions;
using Koan.Core;
using Koan.Core.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Cache.Adapter.Memory.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Cache.Adapter.Memory";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanCacheAdapter("memory");
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
    var capacity = Configuration.Read(cfg, CacheConstants.Configuration.Memory.TagIndexCapacity, 2048);
    var stale = Configuration.Read(cfg, CacheConstants.Configuration.Memory.EnableStaleWhileRevalidate, true);

        report.AddProviderElection("CacheStore", "memory", new[] { "memory", "redis", "custom" }, "Reference = memory adapter package");
        report.AddSetting("TagIndexCapacity", capacity.ToString());
        report.AddSetting("EnableStaleWhileRevalidate", stale.ToString());
    }
}
