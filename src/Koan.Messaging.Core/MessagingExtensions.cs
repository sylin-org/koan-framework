using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Koan.Core.Hosting.App;

namespace Koan.Messaging
{
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
        /// The open generic <see cref="IMessageProxy.SendAsync{T}(T, CancellationToken)"/> method,
        /// specialized per concrete runtime type and cached as a compiled typed delegate. This
        /// preserves the original reflection semantics — <c>SendAsync</c> is dispatched on the
        /// concrete runtime type of the (possibly interceptor-substituted) payload, never on the
        /// static <c>T</c> — while eliminating per-send reflection allocations.
        /// </summary>
        private static readonly MethodInfo SendAsyncOpenMethod =
            typeof(IMessageProxy).GetMethod(nameof(IMessageProxy.SendAsync))
            ?? throw new InvalidOperationException("IMessageProxy.SendAsync<T> not found.");

        private static readonly ConcurrentDictionary<Type, Func<IMessageProxy, object, CancellationToken, Task>> _sendDispatchers = new();

        /// <summary>
        /// Send a message through the Koan messaging system.
        /// Automatically buffers during startup, then routes to live provider.
        /// </summary>
        public static Task Send<T>(this T message, CancellationToken cancellationToken = default) where T : class
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            var intercepted = MessagingInterceptors.Intercept(message);
            var proxy = ResolveMessageProxy();

            // Dispatch SendAsync specialized on the concrete runtime type (identical to the prior
            // MakeGenericMethod(intercepted.GetType()) reflection), via a per-type cached delegate.
            var dispatcher = _sendDispatchers.GetOrAdd(intercepted.GetType(), BuildSendDispatcher);
            return dispatcher(proxy, intercepted, cancellationToken);
        }

        /// <summary>
        /// Builds (once per concrete type) a compiled delegate that invokes
        /// <c>proxy.SendAsync&lt;concreteType&gt;((concreteType)payload, ct)</c>. Equivalent to the
        /// former <c>MakeGenericMethod(concreteType).Invoke(...)</c> but allocation-free per send.
        /// </summary>
        private static Func<IMessageProxy, object, CancellationToken, Task> BuildSendDispatcher(Type concreteType)
        {
            var proxyParam = Expression.Parameter(typeof(IMessageProxy), "proxy");
            var payloadParam = Expression.Parameter(typeof(object), "payload");
            var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

            var typedSendAsync = SendAsyncOpenMethod.MakeGenericMethod(concreteType);
            var call = Expression.Call(
                proxyParam,
                typedSendAsync,
                Expression.Convert(payloadParam, concreteType),
                ctParam);

            return Expression
                .Lambda<Func<IMessageProxy, object, CancellationToken, Task>>(call, proxyParam, payloadParam, ctParam)
                .Compile();
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
        public async Task CreateConsumers(IMessageBus bus, CancellationToken cancellationToken = default)
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