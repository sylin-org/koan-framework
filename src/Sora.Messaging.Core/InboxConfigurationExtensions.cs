using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Core;
using Sora.Core.Modules;
using Sora.Messaging.Infrastructure;

namespace Sora.Messaging;

public static class InboxConfigurationExtensions
{
    public static IServiceCollection AddInboxConfiguration(this IServiceCollection services)
    {
        services.AddSoraOptions<InboxClientOptions>(Constants.Configuration.Inbox.Section);
        services.AddSoraOptions<DiscoveryOptions>(Constants.Configuration.Discovery.Section);
        services.TryAddSingleton<IInboxDiscoveryPolicy, InboxDiscoveryPolicy>();
        return services;
    }
}