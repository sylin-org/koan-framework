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
        return services;
    }
}