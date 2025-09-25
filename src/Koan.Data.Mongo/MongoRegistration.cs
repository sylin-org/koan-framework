using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Core;
using Koan.Core.Adapters;
using Koan.Core.Modules;
using Koan.Data.Abstractions;
using MongoDB.Bson.Serialization.Conventions;

namespace Koan.Data.Mongo;

public static class MongoRegistration
{
    /// <summary>
    /// Register the Mongo adapter for service discovery; optionally configure options.
    /// </summary>
    public static IServiceCollection AddMongoAdapter(this IServiceCollection services, Action<MongoOptions>? configure = null)
    {
        // One-time global conventions
        RegisterConventionsOnce();
        services.AddKoanOptions<MongoOptions>();
        if (configure is not null) services.Configure(configure);
        // Ensure health contributor is available even outside Koan bootstrap
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, MongoHealthContributor>());
        services.AddSingleton<MongoClientProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAsyncAdapterInitializer>(sp => sp.GetRequiredService<MongoClientProvider>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAdapterReadiness>(sp => sp.GetRequiredService<MongoClientProvider>()));
        services.AddSingleton<IDataAdapterFactory, MongoAdapterFactory>();
        return services;
    }

    private static bool _conventionsRegistered;
    private static void RegisterConventionsOnce()
    {
        if (_conventionsRegistered) return;
        var pack = new ConventionPack
        {
            // Allow documents to carry extra fields without breaking deserialization
            new IgnoreExtraElementsConvention(true)
        };
        ConventionRegistry.Register("Koan.IgnoreExtraElements", pack, _ => true);
        _conventionsRegistered = true;
    }
}