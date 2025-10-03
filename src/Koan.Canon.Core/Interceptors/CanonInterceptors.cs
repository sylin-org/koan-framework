using Koan.Data.Core.Model;

namespace Koan.Canon.Core.Interceptors;

/// <summary>
/// Primary entry point for the fluent Canon interceptor API
/// Provides crystal-clear lifecycle timing with comprehensive stage control
/// </summary>
public static class CanonInterceptors
{
    /// <summary>
    /// Begin fluent registration of interceptors for the specified Canon entity type
    /// </summary>
    /// <typeparam name="T">The Canon entity type to register interceptors for</typeparam>
    /// <returns>A fluent builder for registering lifecycle interceptors</returns>
    /// <example>
    /// <code>
    /// CanonInterceptors
    ///   .For&lt;Device&gt;()
    ///     .BeforeIntake(async device =&gt; 
    ///     {
    ///         if (string.IsNullOrEmpty(device.Serial))
    ///             return CanonIntakeActions.Drop(device, "Missing serial");
    ///         return CanonIntakeActions.Continue(device);
    ///     })
    ///     .OnAssociationSuccess(async device =&gt; 
    ///     {
    ///         await NotifyDownstreamSystems(device);
    ///         return CanonStageActions.Continue(device);
    ///     });
    /// </code>
    /// </example>
    public static CanonInterceptorBuilder<T> For<T>() where T : class
    {
        var registry = CanonInterceptorRegistryManager.GetFor<T>();
        return new CanonInterceptorBuilder<T>(registry);
    }
    
    /// <summary>
    /// Check if any interceptors are registered for the specified entity type
    /// Useful for optimization and conditional processing
    /// </summary>
    /// <typeparam name="T">The Canon entity type to check</typeparam>
    /// <returns>True if interceptors are registered for this entity type</returns>
    public static bool HasInterceptorsFor<T>() where T : class
    {
        return CanonInterceptorRegistryManager.HasInterceptorsFor<T>();
    }
    
    /// <summary>
    /// Get all entity types that have registered interceptors
    /// Useful for diagnostics and framework introspection
    /// </summary>
    /// <returns>Enumerable of entity types with registered interceptors</returns>
    public static IEnumerable<Type> GetRegisteredEntityTypes()
    {
        return CanonInterceptorRegistryManager.GetRegisteredEntityTypes();
    }
}

