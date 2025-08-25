using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Core;

namespace Sora.Messaging.RabbitMq;

public static class RabbitMqServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMq(this IServiceCollection services)
    {
        services.AddSingleton<IMessageBusFactory, RabbitMqFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor>(sp =>
            new RabbitMqHealth(sp.GetRequiredService<IMessageBusSelector>(), sp)));
        // Discovery client for Inbox over RabbitMQ (optional)
        services.TryAddSingleton<IInboxDiscoveryClient, RabbitMqInboxDiscoveryClient>();
        // If policy allows discovery and no explicit endpoint configured, attempt discovery and wire HTTP inbox
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISoraInitializer>(sp => new RabbitMqInboxDiscoveryInitializer()));
        return services;
    }
}