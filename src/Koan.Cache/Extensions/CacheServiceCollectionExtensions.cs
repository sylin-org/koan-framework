using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Adapters;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Serialization;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Adapters;
using Koan.Cache.Options;
using Koan.Cache.Policies;
using Koan.Cache.Decorators;
using Koan.Cache.Scope;
using Koan.Cache.Serialization;
using Koan.Cache.Singleflight;
using Koan.Cache.Stores;
using Koan.Cache.Diagnostics;
using Koan.Core.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Data.Core.Decorators;

namespace Koan.Cache.Extensions;

public static class CacheServiceCollectionExtensions
{
    public static IServiceCollection AddKoanCache(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        Action<CacheOptions>? configure = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configuration is not null)
        {
            services.AddKoanOptions<CacheOptions>(configuration, CacheConstants.Configuration.Section, configure);
        }
        else
        {
            services.AddKoanOptions<CacheOptions>(CacheConstants.Configuration.Section);
            if (configure is not null)
            {
                services.PostConfigure(configure);
            }
        }

        services.TryAddSingleton<ICacheScopeAccessor, CacheScopeAccessor>();
        services.TryAddSingleton<CacheSingleflightRegistry>();
        services.TryAddSingleton<CacheClient>();
        services.TryAddSingleton<ICacheClient>(sp => sp.GetRequiredService<CacheClient>());
        services.TryAddSingleton<ICacheReader>(sp => sp.GetRequiredService<CacheClient>());
        services.TryAddSingleton<ICacheWriter>(sp => sp.GetRequiredService<CacheClient>());
        services.TryAddSingleton<ICachePolicyRegistry, CachePolicyRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDataRepositoryDecorator, CacheRepositoryDecorator>());

        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheSerializer, JsonCacheSerializer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheSerializer, StringCacheSerializer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheSerializer, BinaryCacheSerializer>());

        services.TryAddSingleton<CacheInstrumentation>();

        services.TryAddSingleton<ICacheStore>(sp =>
        {
            throw new InvalidOperationException("No cache adapter has been registered. Call AddKoanCacheAdapter(\"memory\") or the provider-specific extension method.");
        });

        services.PostConfigure<CacheOptions>(opts =>
        {
            if (string.IsNullOrWhiteSpace(opts.Provider))
            {
                opts.Provider = "memory";
            }
        });

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<CacheOptions>, CacheOptionsValidator>());

        services.AddHostedService<CachePolicyBootstrapper>();

        return services;
    }

    public static IServiceCollection AddKoanCacheAdapter(this IServiceCollection services, string name, IConfiguration? configuration = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Adapter name must be provided.", nameof(name));
        }

        var registrar = CacheAdapterResolver.Resolve(name);
        var config = configuration ?? new ConfigurationManager();
        registrar.Register(services, config);

        services.AddSingleton(new CacheAdapterDescriptor(name, registrar.GetType()));

        services.PostConfigure<CacheOptions>(opts =>
        {
            opts.Provider = name;
        });

        return services;
    }

    private sealed class CacheOptionsValidator : IConfigureOptions<CacheOptions>
    {
        private readonly ILogger<CacheOptionsValidator> _logger;

        public CacheOptionsValidator(ILogger<CacheOptionsValidator> logger)
        {
            _logger = logger;
        }

        public void Configure(CacheOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.Provider))
            {
                options.Provider = "memory";
            }

            if (options.DefaultSingleflightTimeout <= TimeSpan.Zero)
            {
                _logger.LogWarning("CacheOptions.DefaultSingleflightTimeout was {Timeout}. Resetting to 2 seconds.", options.DefaultSingleflightTimeout);
                options.DefaultSingleflightTimeout = TimeSpan.FromSeconds(2);
            }
        }
    }
}
