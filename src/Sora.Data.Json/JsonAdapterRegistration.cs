using Microsoft.Extensions.DependencyInjection;
using Sora.Core;
using Sora.Core.Modules;
using Sora.Data.Abstractions;

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