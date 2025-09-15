using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Messaging;

/// <summary>
/// Service collection extensions for registering the new Koan messaging system.
/// </summary>
public static class MessagingServiceExtensions
{
    /// <summary>
    /// Adds the core Koan messaging services with developer experience helpers.
    /// Enables <c>.Send()</c> and <c>.On&lt;T&gt;()</c> handler registration patterns.
    /// </summary>
    public static IServiceCollection AddKoanMessaging(this IServiceCollection services)
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