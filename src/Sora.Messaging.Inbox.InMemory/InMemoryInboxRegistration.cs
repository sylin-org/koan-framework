using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sora.Messaging.Inbox;

public static class InMemoryInboxRegistration
{
    public static IServiceCollection AddInMemoryInbox(this IServiceCollection services)
    {
        services.TryAddSingleton<IInboxStore, InMemoryInboxStore>();
        return services;
    }
}