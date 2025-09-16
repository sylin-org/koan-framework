using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Abstractions;

namespace Koan.Data.Json;

public static class JsonDataServiceCollectionExtensions
{
    /// <summary>
    /// Register a JSON repository for a specific aggregate pair.
    /// </summary>
    public static IServiceCollection AddJsonData<TEntity, TKey>(this IServiceCollection services, Action<JsonDataOptions>? configure = null)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        services.AddKoanOptions<JsonDataOptions>();
        if (configure is not null) services.Configure(configure);
        services.AddSingleton<IDataRepository<TEntity, TKey>, JsonRepository<TEntity, TKey>>();
        return services;
    }
}