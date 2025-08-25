using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Core;
using Sora.Data.Abstractions;

namespace Sora.Data.Mongo;

public static class MongoRegistration
{
    /// <summary>
    /// Register the Mongo adapter for service discovery; optionally configure options.
    /// </summary>
    public static IServiceCollection AddMongoAdapter(this IServiceCollection services, Action<MongoOptions>? configure = null)
    {
    services.AddSoraOptions<MongoOptions>();
        if (configure is not null) services.Configure(configure);
        // Ensure health contributor is available even outside Sora bootstrap
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, MongoHealthContributor>());
        services.AddSingleton<IDataAdapterFactory, MongoAdapterFactory>();
        return services;
    }
}