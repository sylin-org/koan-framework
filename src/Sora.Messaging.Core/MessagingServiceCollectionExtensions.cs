using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Core;
using Sora.Core.Modules;
using Sora.Messaging.Infrastructure;

namespace Sora.Messaging;

public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddMessagingCore(this IServiceCollection services)
    {
        services.AddSoraOptions<MessagingOptions>(Constants.Configuration.Section);
        services.TryAddSingleton<IMessageBusSelector, MessageBusSelector>();
        services.TryAddSingleton<ITypeAliasRegistry, DefaultTypeAliasRegistry>();
        services.TryAddSingleton<IMessagingDiagnostics, MessagingDiagnostics>();
        // Topology naming & provisioner defaults (providers can override)
        services.TryAddSingleton<Provisioning.ITopologyNaming, DefaultTopologyNaming>();
        services.TryAddSingleton<Provisioning.ITopologyProvisioner, NoopTopologyProvisioner>();
        // Register MessagingReadinessProvider for auto-registration
        services.TryAddSingleton<Provisioning.IMessagingReadinessProvider, Provisioning.MessagingReadinessProvider>();
        services.AddHostedService<Sora.Messaging.Provisioning.TopologyOrchestratorHostedService>();

        // Register DefaultTopologyPlanner with all dependencies
        services.TryAddSingleton<Provisioning.ITopologyPlanner>(sp =>
            new Core.Provisioning.DefaultTopologyPlanner(
                sp.GetRequiredService<Provisioning.ITopologyProvisioner>(),
                sp.GetRequiredService<Provisioning.ITopologyNaming>(),
                sp.GetRequiredService<ITypeAliasRegistry>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MessagingOptions>>()));

        // Register hosted service to run planner at startup
        services.AddHostedService<Core.Provisioning.TopologyPlannerHostedService>();

        return services;
    }
}