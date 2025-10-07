using System;
using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Adapters;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Adapter.Memory.Options;
using Koan.Cache.Adapter.Memory.Stores;
using Koan.Cache.Extensions;
using Koan.Core.Modules;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Cache.Adapter.Memory;

public sealed class MemoryCacheAdapterRegistrar : ICacheAdapterRegistrar
{
    public string Name => "memory";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddKoanOptions<MemoryCacheAdapterOptions>(configuration, CacheConstants.Configuration.Memory.Section);

        services.AddMemoryCache();

        services.TryAddSingleton<MemoryCacheStore>();
        services.AddSingleton<ICacheStore>(sp => sp.GetRequiredService<MemoryCacheStore>());

        services.AddSingleton(new CacheAdapterDescriptor(Name, GetType(), "In-memory cache adapter"));
    }
}
