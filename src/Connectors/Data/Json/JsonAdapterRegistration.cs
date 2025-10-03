using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Abstractions;

namespace Koan.Data.Connector.Json;

public static class JsonAdapterRegistration
{
    /// <summary>
    /// Register the JSON adapter for discovery; optionally configure options.
    /// </summary>
    public static IServiceCollection AddJsonAdapter(this IServiceCollection services, Action<JsonDataOptions>? configure = null)
    {
        services.AddKoanOptions<JsonDataOptions>("Koan:Data:Json");
        if (configure is not null) services.Configure(configure);
        services.AddSingleton<IDataAdapterFactory, JsonAdapterFactory>();
        return services;
    }
}
