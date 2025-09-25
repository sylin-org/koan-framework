using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Core;
using Koan.Core.Adapters;
using Koan.Core.Modules;
using Koan.Data.Abstractions;

namespace Koan.Data.Couchbase;

public static class CouchbaseRegistration
{
    public static IServiceCollection AddCouchbaseAdapter(this IServiceCollection services, Action<CouchbaseOptions>? configure = null)
    {
        services.AddKoanOptions<CouchbaseOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, CouchbaseHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Koan.Data.Abstractions.Naming.INamingDefaultsProvider, CouchbaseNamingDefaultsProvider>());
        services.AddSingleton<CouchbaseClusterProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAsyncAdapterInitializer, CouchbaseClusterProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAdapterReadiness, CouchbaseClusterProvider>());
        services.AddSingleton<IDataAdapterFactory, CouchbaseAdapterFactory>();
        return services;
    }
}
