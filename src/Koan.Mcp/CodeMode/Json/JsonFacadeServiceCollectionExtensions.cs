using Microsoft.Extensions.DependencyInjection;

namespace Koan.Mcp.CodeMode.Json;

internal static class JsonFacadeServiceCollectionExtensions
{
    public static IServiceCollection AddCodeModeJson(this IServiceCollection services)
    {
        services.AddSingleton<IJsonFacade, NewtonsoftJsonFacade>();
        return services;
    }
}
