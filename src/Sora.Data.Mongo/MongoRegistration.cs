using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Core;
using Sora.Core.Modules;
using Sora.Data.Abstractions;
using MongoDB.Bson.Serialization.Conventions;

namespace Sora.Data.Mongo;

public static class MongoRegistration
{
    /// <summary>
    /// Register the Mongo adapter for service discovery; optionally configure options.
    /// </summary>
    public static IServiceCollection AddMongoAdapter(this IServiceCollection services, Action<MongoOptions>? configure = null)
    {
        // One-time global conventions
        RegisterConventionsOnce();
        services.AddSoraOptions<MongoOptions>();
        if (configure is not null) services.Configure(configure);
        // Ensure health contributor is available even outside Sora bootstrap
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, MongoHealthContributor>());
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
        ConventionRegistry.Register("Sora.IgnoreExtraElements", pack, _ => true);
        _conventionsRegistered = true;
    }
}