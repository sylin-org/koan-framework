using Koan.Communication.Infrastructure;
using Koan.Communication.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Communication;

/// <summary>Registers Koan Communication and its built-in process-local Transport floor.</summary>
public static class CommunicationServiceCollectionExtensions
{
    public static IServiceCollection AddKoanCommunication(
        this IServiceCollection services,
        Action<CommunicationOptions>? configure = null)
    {
        var options = services.AddOptions<CommunicationOptions>()
            .BindConfiguration(Constants.Configuration.Section)
            .Validate(
                static value => value.InProcessCapacity > 0,
                $"{nameof(CommunicationOptions.InProcessCapacity)} must be greater than zero.")
            .Validate(
                static value => value.MaxPayloadBytes > 0,
                $"{nameof(CommunicationOptions.MaxPayloadBytes)} must be greater than zero.")
            .ValidateOnStart();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        var registry = TransportReceiverRegistry.FromDiscovery();
        services.TryAddSingleton(registry);
        foreach (var handlerType in registry.HandlerTypes)
        {
            services.TryAdd(ServiceDescriptor.Scoped(handlerType, handlerType));
        }

        services.TryAddSingleton<TransportIngress>();
        services.TryAddSingleton<InProcessTransportRuntime>();
        services.TryAddSingleton<TransportCoordinator>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, CommunicationHostedService>());
        return services;
    }
}
