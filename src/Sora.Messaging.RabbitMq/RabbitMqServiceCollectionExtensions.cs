using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Core;
using Sora.Messaging.Provisioning;
using RabbitMQ.Client;

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
    // Register RabbitMQ topology provisioner (scoped basic provisioner for advanced queue declares)
    services.AddScoped<ITopologyProvisioner, Sora.Messaging.RabbitMq.Provisioning.RabbitMqTopologyProvisioner>();
    // Register full planner/inspector/differ/applier so core orchestrator can drive diff/apply generically
    // Always add provider-specific planner (will override single-service resolution order while preserving enumeration with default planner if needed)
    services.AddSingleton<ITopologyPlanner, Sora.Messaging.RabbitMq.Provisioning.RabbitMqProvisioner>();
    services.TryAddSingleton<ITopologyInspector, Sora.Messaging.RabbitMq.Provisioning.RabbitMqProvisioner>();
    services.TryAddSingleton<ITopologyDiffer, Sora.Messaging.RabbitMq.Provisioning.RabbitMqProvisioner>();
    services.TryAddSingleton<ITopologyApplier, Sora.Messaging.RabbitMq.Provisioning.RabbitMqProvisioner>();
    // Provider client accessor for orchestrator diff/apply pipeline
    services.TryAddSingleton<Sora.Messaging.Provisioning.IProviderClientAccessor, Sora.Messaging.RabbitMq.Provisioning.RabbitMqProviderClientAccessor>();

        // Explicit connection & channel singletons for components (e.g. topology provisioner) that require direct RabbitMQ primitives.
        // These are sourced from the provider client accessor to avoid duplicating connection logic/config parsing.
        services.TryAddSingleton<IConnection>(sp =>
        {
            var accessor = sp.GetRequiredService<IProviderClientAccessor>();
            var client = accessor.GetProviderClient("default")
                         ?? throw new InvalidOperationException("RabbitMQ provider client (default bus) unavailable");
            if (client is (IConnection conn, IModel _, RabbitMqOptions _)) return conn;
            var tuple = ((IConnection, IModel, RabbitMqOptions))client; // fallback cast
            return tuple.Item1;
        });
        services.TryAddSingleton<IModel>(sp =>
        {
            var accessor = sp.GetRequiredService<IProviderClientAccessor>();
            var client = accessor.GetProviderClient("default")
                         ?? throw new InvalidOperationException("RabbitMQ provider client (default bus) unavailable");
            if (client is (IConnection _, IModel ch, RabbitMqOptions _)) return ch;
            var tuple = ((IConnection, IModel, RabbitMqOptions))client;
            return tuple.Item2;
        });

        // The Sora Way: Auto-detect message handlers and ensure consumers exist
        services.AddHostedService<RabbitMqConsumerAutoStarter>();

        return services;
    }
}