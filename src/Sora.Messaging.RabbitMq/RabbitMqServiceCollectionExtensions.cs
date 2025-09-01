using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Core;

namespace Sora.Messaging.RabbitMq;

public static class RabbitMqServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMq(this IServiceCollection services)
    {
        services.AddSingleton<IMessageBusFactory, RabbitMqFactory>();
    // Register RabbitMQ health contributor once (enumerable-friendly registration requires concrete implementation type)
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, RabbitMqHealth>());
        // Discovery client for Inbox over RabbitMQ (optional)
        services.TryAddSingleton<IInboxDiscoveryClient, RabbitMqInboxDiscoveryClient>();
        // If policy allows discovery and no explicit endpoint configured, attempt discovery and wire HTTP inbox
    services.TryAddEnumerable(ServiceDescriptor.Singleton<ISoraInitializer, RabbitMqInboxDiscoveryInitializer>());
        return services;
    }
}