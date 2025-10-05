using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Koan.Data.Core.Model;

namespace Koan.Canon.Core.Interceptors;

/// <summary>
/// Central registry manager for all Canon entity type interceptors
/// </summary>
internal static class CanonInterceptorRegistryManager
{
    private static readonly ConcurrentDictionary<Type, ICanonInterceptorRegistry> _registries = new();
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
    public static CanonInterceptorRegistry<T> GetFor<T>() where T : class
    {
        return (CanonInterceptorRegistry<T>)_registries.GetOrAdd(typeof(T), entityType =>
        {
            // Create registry with dependency injection support
            var logger = _serviceProvider?.GetService<ILogger<CanonInterceptorRegistry<T>>>();
            if (logger == null)
            {
                // Fallback logger factory if service provider not available
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                logger = loggerFactory.CreateLogger<CanonInterceptorRegistry<T>>();
            }
            
            return new CanonInterceptorRegistry<T>(logger);
        });
    }
    
    /// <summary>
    /// Get the interceptor registry for the specified entity type (non-generic)
    /// </summary>
    public static ICanonInterceptorRegistry? GetFor(Type entityType)
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
            
        return ((CanonInterceptorRegistry<T>)registry).HasInterceptors();
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
internal interface ICanonInterceptorRegistry
{
    bool HasInterceptors();
    bool HasBeforeIntakeNonGeneric();
    Task<object?> ExecuteBeforeIntakeNonGeneric(object entity);
}

