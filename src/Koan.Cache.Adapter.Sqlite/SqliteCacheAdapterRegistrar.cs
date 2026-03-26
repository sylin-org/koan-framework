using Koan.Cache.Abstractions.Adapters;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Adapter.Sqlite.Options;
using Koan.Cache.Adapter.Sqlite.Stores;
using Koan.Core.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Cache.Adapter.Sqlite;

public sealed class SqliteCacheAdapterRegistrar : ICacheAdapterRegistrar
{
    public string Name => "sqlite";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddKoanOptions<SqliteCacheOptions>(configuration, "Koan:Cache:Adapters:Sqlite");
        services.TryAddSingleton<SqliteCacheStore>();
        services.AddSingleton<ICacheStore>(sp => sp.GetRequiredService<SqliteCacheStore>());
        services.AddSingleton(new CacheAdapterDescriptor(Name, GetType(), "Persistent local SQLite cache"));
    }
}
