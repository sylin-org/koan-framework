using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.AI.Web;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKoanAiWeb(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AiHealthSubscriber>());
        return services;
    }
}
