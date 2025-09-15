using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Koan.Data.Core.Model;

namespace Koan.Flow.Core.Interceptors;

/// <summary>
/// Central registry manager for all Flow entity type interceptors
/// </summary>
internal static class FlowInterceptorRegistryManager
{
    private static readonly ConcurrentDictionary<Type, IFlowInterceptorRegistry> _registries = new();
    private static IServiceProvider? _serviceProvider;
    
    /// <summary>
    /// Initialize the registry manager with service provider for dependency injection
    /// </summary>
    public static void Initialize(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    
    /// <summary>
    /// Get or create the interceptor registry for the specified entity type
    /// </summary>
    public static FlowInterceptorRegistry<T> GetFor<T>() where T : class
    {
        return (FlowInterceptorRegistry<T>)_registries.GetOrAdd(typeof(T), entityType =>
        {
            // Create registry with dependency injection support
            var logger = _serviceProvider?.GetService<ILogger<FlowInterceptorRegistry<T>>>();
            if (logger == null)
            {
                // Fallback logger factory if service provider not available
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                logger = loggerFactory.CreateLogger<FlowInterceptorRegistry<T>>();
            }
            
            return new FlowInterceptorRegistry<T>(logger);
        });
    }
    
    /// <summary>
    /// Get the interceptor registry for the specified entity type (non-generic)
    /// </summary>
    public static IFlowInterceptorRegistry? GetFor(Type entityType)
    {
        return _registries.TryGetValue(entityType, out var registry) ? registry : null;
    }
    
    /// <summary>
    /// Check if any interceptors are registered for the specified entity type
    /// </summary>
    public static bool HasInterceptorsFor<T>() where T : class
    {
        if (!_registries.TryGetValue(typeof(T), out var registry))
            return false;
            
        return ((FlowInterceptorRegistry<T>)registry).HasInterceptors();
    }
    
    /// <summary>
    /// Check if any interceptors are registered for any entity type
    /// </summary>
    public static bool HasAnyInterceptors()
    {
        return _registries.Values.Any(registry => 
            registry.GetType().GetMethod("HasInterceptors")
                ?.Invoke(registry, null) is true);
    }
    
    /// <summary>
    /// Get all registered entity types that have interceptors
    /// </summary>
    public static IEnumerable<Type> GetRegisteredEntityTypes()
    {
        return _registries.Keys.Where(type => 
        {
            var registry = _registries[type];
            return registry.GetType().GetMethod("HasInterceptors")
                ?.Invoke(registry, null) is true;
        });
    }
    
    /// <summary>
    /// Clear all registered interceptors (primarily for testing)
    /// </summary>
    internal static void ClearAll()
    {
        _registries.Clear();
    }
}

/// <summary>
/// Marker interface for type-safe registry storage
/// </summary>
internal interface IFlowInterceptorRegistry
{
    bool HasInterceptors();
    bool HasBeforeIntakeNonGeneric();
    Task<object?> ExecuteBeforeIntakeNonGeneric(object entity);
}