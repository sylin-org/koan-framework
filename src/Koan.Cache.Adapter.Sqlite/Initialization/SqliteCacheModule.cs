using Koan.Cache.Adapter.Sqlite.Options;
using Koan.Cache.Adapter.Sqlite.Stores;
using Koan.Cache.Adapter.Sqlite.Infrastructure;
using Koan.Cache.Abstractions.Stores;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Cache.Adapter.Sqlite.Initialization;

/// <summary>
/// Reference = Intent: referencing <c>Koan.Cache.Adapter.Sqlite</c> auto-registers
/// <see cref="SqliteCacheStore"/> as a Local-tier <c>ICacheStore</c>. With higher
/// <c>[ProviderPriority]</c> than the in-process Memory store, SQLite becomes the
/// default L1 when this package is referenced — providing persistence across restarts.
/// </summary>
public sealed class SqliteCacheModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<SqliteCacheOptions>(Constants.Configuration.Section);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheStore, SqliteCacheStore>());
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);

        var databasePath = Configuration.Read(
            cfg,
            Constants.Configuration.DatabasePath,
            Constants.Configuration.DefaultDatabasePath);

        module.AddSetting("CacheStore", $"{Constants.ProviderId} (Local, [ProviderPriority({Constants.ProviderPriority})])");
        module.AddSetting("DatabasePath", databasePath);
    }
}
