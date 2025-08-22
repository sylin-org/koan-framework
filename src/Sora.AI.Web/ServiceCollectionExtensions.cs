using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Sora.AI.Web;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSoraAiWeb(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AiHealthSubscriber>());
        return services;
    }
}
