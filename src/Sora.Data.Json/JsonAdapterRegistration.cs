using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Sora.Core;

namespace Sora.Data.Json;

public static class JsonAdapterRegistration
{
    /// <summary>
    /// Register the JSON adapter for discovery; optionally configure options.
    /// </summary>
    public static IServiceCollection AddJsonAdapter(this IServiceCollection services, Action<JsonDataOptions>? configure = null)
    {
    services.AddSoraOptions<JsonDataOptions>("Sora:Data:Json");
        if (configure is not null) services.Configure(configure);
        services.AddSingleton<IDataAdapterFactory, JsonAdapterFactory>();
        return services;
    }
}