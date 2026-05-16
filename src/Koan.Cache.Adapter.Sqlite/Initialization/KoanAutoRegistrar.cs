using Koan.Cache.Adapter.Sqlite.Options;
using Koan.Cache.Adapter.Sqlite.Stores;
using Koan.Cache.Abstractions.Extensions;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Cache.Adapter.Sqlite.Initialization;

/// <summary>
/// Reference = Intent: referencing <c>Koan.Cache.Adapter.Sqlite</c> auto-registers
/// <see cref="SqliteCacheStore"/> as a Local-tier <c>ICacheStore</c>. With higher
/// <c>[ProviderPriority]</c> than the in-process Memory store, SQLite becomes the
/// default L1 when this package is referenced — providing persistence across restarts.
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Cache.Adapter.Sqlite";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<SqliteCacheOptions>("Koan:Cache:Adapters:Sqlite");
        services.AddCacheStore<SqliteCacheStore>();
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);

        var databasePath = Configuration.Read(cfg, "Koan:Cache:Adapters:Sqlite:DatabasePath", ".Koan/cache/cache.db");
        var sweepInterval = Configuration.Read(cfg, "Koan:Cache:Adapters:Sqlite:SweepIntervalSeconds", 60);

        module.AddSetting("CacheStore", "sqlite (Local, [ProviderPriority(50)])");
        module.AddSetting("DatabasePath", databasePath);
        module.AddSetting("SweepIntervalSeconds", sweepInterval.ToString());
    }
}
