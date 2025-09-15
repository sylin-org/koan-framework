using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Koan.Core.Hosting.App;
using Koan.Messaging.Contracts;

namespace Koan.Messaging
{
    public static class MessagingTransformers
    {
        private static readonly ConcurrentDictionary<string, Func<object, object>> _registry = new();

        public static void Register(string descriptor, Func<object, object> transformer)
            => _registry[descriptor] = transformer;

        public static object Transform(string descriptor, object payload)
            => _registry.TryGetValue(descriptor, out var fn) ? fn(payload) : payload;

        public static Func<object, object>? GetTransformer(string descriptor)
            => _registry.TryGetValue(descriptor, out var fn) ? fn : null;
    }

    public static class MessagingInterceptors
    {
    private static readonly ConcurrentDictionary<Type, Func<object, object>> _typeRegistry = new();
    private static readonly ConcurrentDictionary<Type, Func<object, object>> _interfaceRegistry = new();
    private static readonly ConcurrentDictionary<Type, Type[]> _typeCache = new();
    // Caches the top-level concrete type name (FullName preferred, falling back to Name) for fast lookup
    private static readonly ConcurrentDictionary<Type, string> _concreteTypeNameCache = new();

        public static void RegisterForType<T>(Func<T, object> interceptor)
            => _typeRegistry[typeof(T)] = obj => interceptor((T)obj);

        public static void RegisterForInterface<T>(Func<T, object> interceptor)
            => _interfaceRegistry[typeof(T)] = obj => interceptor((T)obj);

        public static object Intercept(object payload)
        {
            var type = payload.GetType();
            // Check type registry
            if (_typeRegistry.TryGetValue(type, out var typeInterceptor))
                return typeInterceptor(payload);
            // Check interface registry
            var interfaces = _typeCache.GetOrAdd(type, t => t.GetInterfaces());
            foreach (var iface in interfaces)
            {
                if (_interfaceRegistry.TryGetValue(iface, out var ifaceInterceptor))
                    return ifaceInterceptor(payload);
            }
            // No match, return original
            return payload;
        }

        /// <summary>
        /// Returns a cached canonical name for the concrete (runtime) type of the payload.
    /// Uses <see cref="P:System.Type.FullName"/> if available, otherwise <see cref="P:System.Type.Name"/>.
        /// </summary>
        public static string GetConcreteTypeName(object payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            var type = payload.GetType();
            return _concreteTypeNameCache.GetOrAdd(type, static t => t.FullName ?? t.Name);
        }
    }

    /// <summary>
    /// Beautiful developer experience extensions for the Koan messaging system.
    /// Provides <c>.Send()</c> and <c>.On&lt;T&gt;()</c> patterns for zero-config messaging.
    /// </summary>
    public static class MessagingExtensions
    {
        /// <summary>
        /// Send a message through the Koan messaging system.
        /// Automatically buffers during startup, then routes to live provider.
        /// </summary>
        public static async Task Send<T>(this T message, string descriptor = default!, CancellationToken cancellationToken = default) where T : class
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            // If descriptor is provided, transform; else send as-is
            var transformed = descriptor != null ? MessagingTransformers.Transform(descriptor, message) : message;

            var intercepted = MessagingInterceptors.Intercept(transformed);
            var proxy = ResolveMessageProxy();

            // NEW: Check for queue-specific routing
            if (intercepted is IQueuedMessage queuedMessage)
            {
                // Route to specific queue
                await SendToQueueAsync(proxy, queuedMessage.QueueName, queuedMessage.Payload, cancellationToken);
            }
            else
            {
                // Default behavior: type-based routing
                var concreteType = intercepted.GetType();
                var sendAsyncMethod = proxy.GetType().GetMethod("SendAsync");
                if (sendAsyncMethod == null)
                    throw new InvalidOperationException($"SendAsync method not found for type {concreteType.Name}");
                var genericSendAsync = sendAsyncMethod.MakeGenericMethod(concreteType);
                await (Task)genericSendAsync!.Invoke(proxy, new object[] { intercepted, cancellationToken })!;
            }
        }

        /// <summary>
        /// Sends a message to a specific queue using the provider's queue-specific routing.
        /// </summary>
        private static async Task SendToQueueAsync(object proxy, string queueName, object payload, CancellationToken cancellationToken)
        {
            // Try to find SendToQueueAsync method on the provider
            var sendToQueueMethod = proxy.GetType().GetMethod("SendToQueueAsync");
            if (sendToQueueMethod != null)
            {
                // Provider supports queue-specific routing
                var payloadType = payload.GetType();
                var genericSendToQueue = sendToQueueMethod.MakeGenericMethod(payloadType);
                await (Task)genericSendToQueue!.Invoke(proxy, new object[] { queueName, payload, cancellationToken })!;
            }
            else
            {
                // Fallback to regular SendAsync (for providers that don't support queue routing yet)
                var concreteType = payload.GetType();
                var sendAsyncMethod = proxy.GetType().GetMethod("SendAsync");
                if (sendAsyncMethod == null)
                    throw new InvalidOperationException($"Neither SendToQueueAsync nor SendAsync method found on provider {proxy.GetType().Name}");
                var genericSendAsync = sendAsyncMethod.MakeGenericMethod(concreteType);
                await (Task)genericSendAsync!.Invoke(proxy, new object[] { payload, cancellationToken })!;
            }
        }
        
        /// <summary>
        /// Register a message handler using fluent syntax via <c>services.On&lt;T&gt;(handler)</c>.
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
                ?? throw new InvalidOperationException("AppHost.Current is not initialized. Ensure AddKoan() was called during startup.");
                
            var proxy = serviceProvider.GetService<IMessageProxy>()
                ?? throw new InvalidOperationException("IMessageProxy is not registered. Ensure AddKoan() and messaging core services are configured.");
                
            return proxy;
        }
    }

    /// <summary>
    /// Registry for message handlers configured during startup.
    /// Handlers are registered via <c>.On&lt;T&gt;()</c> extension and then used to create consumers.
    /// </summary>
    public class HandlerRegistry
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Delegate> _handlers = new();
        private readonly ILogger? _logger;
        
        public HandlerRegistry() 
        {
            _logger = null;
        }
        
        public HandlerRegistry(ILogger logger)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// Adds a handler for the specified message type.
        /// </summary>
        public void AddHandler<T>(Type messageType, Func<T, Task> handler) where T : class
        {
            var added = _handlers.TryAdd(messageType, handler);
            try
            {
                var typeName = messageType.FullName ?? messageType.Name;
                if (_logger != null)
                {
                    _logger.LogInformation("{Status}: {TypeName}", added ? "Added" : "Skipped (exists)", typeName);
                }
                else
                {
                    Console.WriteLine($"[Messaging][Registry] {(added ? "Added" : "Skipped (exists)")}: {typeName}");
                }
            }
            catch { /* non-fatal */ }
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
}