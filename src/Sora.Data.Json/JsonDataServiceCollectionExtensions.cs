using Microsoft.Extensions.DependencyInjection;
using Sora.Core;
using Sora.Data.Abstractions;

namespace Sora.Data.Json;

public static class JsonDataServiceCollectionExtensions
{
    /// <summary>
    /// Register a JSON repository for a specific aggregate pair.
    /// </summary>
    public static IServiceCollection AddJsonData<TEntity, TKey>(this IServiceCollection services, Action<JsonDataOptions>? configure = null)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
    services.AddSoraOptions<JsonDataOptions>();
        if (configure is not null) services.Configure(configure);
        services.AddSingleton<IDataRepository<TEntity, TKey>, JsonRepository<TEntity, TKey>>();
        return services;
    }
}