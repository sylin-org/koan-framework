using Sora.Data.Core.Model;

namespace Sora.Flow.Core.Interceptors;

/// <summary>
/// Primary entry point for the fluent Flow interceptor API
/// Provides crystal-clear lifecycle timing with comprehensive stage control
/// </summary>
public static class FlowInterceptors
{
    /// <summary>
    /// Begin fluent registration of interceptors for the specified Flow entity type
    /// </summary>
    /// <typeparam name="T">The Flow entity type to register interceptors for</typeparam>
    /// <returns>A fluent builder for registering lifecycle interceptors</returns>
    /// <example>
    /// <code>
    /// FlowInterceptors
    ///   .For&lt;Device&gt;()
    ///     .BeforeIntake(async device =&gt; 
    ///     {
    ///         if (string.IsNullOrEmpty(device.Serial))
    ///             return FlowIntakeActions.Drop(device, "Missing serial");
    ///         return FlowIntakeActions.Continue(device);
    ///     })
    ///     .OnAssociationSuccess(async device =&gt; 
    ///     {
    ///         await NotifyDownstreamSystems(device);
    ///         return FlowStageActions.Continue(device);
    ///     });
    /// </code>
    /// </example>
    public static FlowInterceptorBuilder<T> For<T>() where T : class
    {
        var registry = FlowInterceptorRegistryManager.GetFor<T>();
        return new FlowInterceptorBuilder<T>(registry);
    }
    
    /// <summary>
    /// Check if any interceptors are registered for the specified entity type
    /// Useful for optimization and conditional processing
    /// </summary>
    /// <typeparam name="T">The Flow entity type to check</typeparam>
    /// <returns>True if interceptors are registered for this entity type</returns>
    public static bool HasInterceptorsFor<T>() where T : class
    {
        return FlowInterceptorRegistryManager.HasInterceptorsFor<T>();
    }
    
    /// <summary>
    /// Get all entity types that have registered interceptors
    /// Useful for diagnostics and framework introspection
    /// </summary>
    /// <returns>Enumerable of entity types with registered interceptors</returns>
    public static IEnumerable<Type> GetRegisteredEntityTypes()
    {
        return FlowInterceptorRegistryManager.GetRegisteredEntityTypes();
    }
}