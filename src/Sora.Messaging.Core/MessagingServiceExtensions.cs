using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sora.Messaging;

/// <summary>
/// Service collection extensions for registering the new Sora messaging system.
/// </summary>
public static class MessagingServiceExtensions
{
    /// <summary>
    /// Adds the core Sora messaging services with beautiful developer experience.
    /// Enables .Send() extensions and .On<T>() handler registration.
    /// </summary>
    public static IServiceCollection AddSoraMessaging(this IServiceCollection services)
    {
        // Core messaging services
        services.TryAddSingleton<IMessageBuffer, InMemoryMessageBuffer>();
        services.TryAddSingleton<AdaptiveMessageProxy>();
        services.TryAddSingleton<IMessageProxy>(provider => provider.GetRequiredService<AdaptiveMessageProxy>());
        
        // Handler registry for .On<T>() pattern
        services.Configure<HandlerRegistry>(_ => { }); // Initialize empty registry
        
        // Messaging lifecycle management
        services.AddMessagingLifecycle();
        
        return services;
    }
}