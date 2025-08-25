using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Core;

namespace Sora.Messaging;

public static class InboxConfigurationExtensions
{
    public static IServiceCollection AddInboxConfiguration(this IServiceCollection services)
    {
    services.AddSoraOptions<InboxClientOptions>("Sora:Messaging:Inbox");
    services.AddSoraOptions<DiscoveryOptions>("Sora:Messaging:Discovery");
        services.TryAddSingleton<IInboxDiscoveryPolicy, InboxDiscoveryPolicy>();
        return services;
    }
}