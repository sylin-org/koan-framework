using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sora.Core.Hosting.App;

namespace Sora.Messaging;

/// <summary>
/// Beautiful developer experience extensions for the Sora messaging system.
/// Provides .Send() and .On<T>() patterns for zero-config messaging.
/// </summary>
public static class MessagingExtensions
{
    /// <summary>
    /// Send a message through the Sora messaging system.
    /// Automatically buffers during startup, then routes to live provider.
    /// </summary>
    public static async Task Send<T>(this T message, CancellationToken cancellationToken = default) where T : class
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));
        
        var proxy = ResolveMessageProxy();
        await proxy.SendAsync(message, cancellationToken);
    }
    
    /// <summary>
    /// Register a message handler using beautiful fluent syntax.
    /// </summary>
    public static IServiceCollection On<T>(this IServiceCollection services, Func<T, Task> handler) where T : class
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));
            
        services.Configure<HandlerRegistry>(registry => 
        {
            registry.AddHandler(typeof(T), handler);
        });
        
        return services;
    }
    
    private static IMessageProxy ResolveMessageProxy()
    {
        var serviceProvider = AppHost.Current 
            ?? throw new InvalidOperationException("AppHost.Current is not initialized. Ensure AddSora() was called during startup.");
            
        var proxy = serviceProvider.GetService<IMessageProxy>()
            ?? throw new InvalidOperationException("IMessageProxy is not registered. Ensure AddSora() and messaging core services are configured.");
            
        return proxy;
    }
}

/// <summary>
/// Registry for message handlers configured during startup.
/// Handlers are registered via .On<T>() extension and then used to create consumers.
/// </summary>
public class HandlerRegistry
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Delegate> _handlers = new();
    
    /// <summary>
    /// Adds a handler for the specified message type.
    /// </summary>
    public void AddHandler<T>(Type messageType, Func<T, Task> handler) where T : class
    {
        _handlers.TryAdd(messageType, handler);
    }
    
    /// <summary>
    /// Gets all registered handlers.
    /// </summary>
    public System.Collections.Generic.Dictionary<Type, Delegate> GetAllHandlers()
    {
        return new System.Collections.Generic.Dictionary<Type, Delegate>(_handlers);
    }
    
    /// <summary>
    /// Creates message consumers for all registered handlers.
    /// Called during Phase 3 of the messaging lifecycle.
    /// </summary>
    public async Task CreateConsumersAsync(IMessageBus bus, CancellationToken cancellationToken = default)
    {
        foreach (var (messageType, handler) in _handlers)
        {
            try
            {
                // Use reflection to call CreateConsumerAsync<T> with the correct type
                var createConsumerMethod = typeof(IMessageBus)
                    .GetMethod(nameof(IMessageBus.CreateConsumerAsync))!
                    .MakeGenericMethod(messageType);
                
                await (Task)createConsumerMethod.Invoke(bus, new object[] { handler, cancellationToken })!;
            }
            catch (Exception ex)
            {
                // Log error but continue creating other consumers
                System.Diagnostics.Debug.WriteLine($"Failed to create consumer for {messageType.Name}: {ex.Message}");
            }
        }
    }
}