using Koan.Cache.Extensions;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Cache.Adapter.Sqlite.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Cache.Adapter.Sqlite";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanCacheAdapter("sqlite");
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);

        var databasePath = Configuration.Read(cfg, "Koan:Cache:Adapters:Sqlite:DatabasePath", ".Koan/cache/cache.db");
        var sweepInterval = Configuration.Read(cfg, "Koan:Cache:Adapters:Sqlite:SweepIntervalSeconds", 60);

        module.AddSetting("CacheStore.Selected", "sqlite");
        module.AddSetting("CacheStore.Candidates", "memory, redis, sqlite, custom");
        module.AddSetting("CacheStore.Rationale", "Reference = sqlite adapter package");
        module.AddSetting("DatabasePath", databasePath);
        module.AddSetting("SweepIntervalSeconds", sweepInterval.ToString());
    }
}
