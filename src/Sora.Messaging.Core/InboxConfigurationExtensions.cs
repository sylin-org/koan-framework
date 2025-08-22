using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sora.Messaging;

public static class InboxConfigurationExtensions
{
    public static IServiceCollection AddInboxConfiguration(this IServiceCollection services)
    {
        services.AddOptions<InboxClientOptions>().BindConfiguration("Sora:Messaging:Inbox");
        services.AddOptions<DiscoveryOptions>().BindConfiguration("Sora:Messaging:Discovery");
        services.TryAddSingleton<IInboxDiscoveryPolicy, InboxDiscoveryPolicy>();
        return services;
    }
}