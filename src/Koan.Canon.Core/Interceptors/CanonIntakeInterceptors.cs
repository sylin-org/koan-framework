using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Koan.Canon.Core.Interceptors;

/// <summary>
/// Result from a Canon intake interceptor indicating how to handle the payload.
/// </summary>
public record CanonIntakeResult(
    object Payload,
    bool MustDrop = false,
    string? ParkingStatus = null
);

/// <summary>
/// Action types for Canon intake interceptors
/// </summary>
public enum CanonIntakeActionType
{
    Continue,
    Drop,
    Park,
    Transform
}

/// <summary>
/// Represents the result of a Canon intake interceptor execution
/// </summary>
public class CanonIntakeAction
{
    public CanonIntakeActionType Action { get; }
    public object Entity { get; }
    public string? Reason { get; }
    public object? OriginalEntity { get; set; }
    
    /// <summary>Indicates if this action should stop the intake processing</summary>
    public bool ShouldStop => Action != CanonIntakeActionType.Continue;
    
    public CanonIntakeAction(CanonIntakeActionType action, object entity, string? reason = null)
    {
        Action = action;
        Entity = entity;
        Reason = reason;
    }
}

/// <summary>
/// Enhanced helper methods for creating Canon intake actions
/// </summary>
public static class CanonIntakeActions
{
    /// <summary>
    /// Continue normal processing with the given entity.
    /// </summary>
    public static CanonIntakeAction Continue(object entity) => new(CanonIntakeActionType.Continue, entity);
    
    /// <summary>
    /// Drop the entity entirely - skip all processing.
    /// </summary>
    public static CanonIntakeAction Drop(object entity, string? reason = null) => new(CanonIntakeActionType.Drop, entity, reason);
    
    /// <summary>
    /// Park the entity with the specified status for later processing.
    /// </summary>
    public static CanonIntakeAction Park(object entity, string reasonCode, string? evidence = null) => 
        new(CanonIntakeActionType.Park, entity, reasonCode) { OriginalEntity = evidence };
    
    /// <summary>
    /// Transform the entity before processing.
    /// </summary>
    public static CanonIntakeAction Transform(object original, object transformed, string? reason = null) =>
        new(CanonIntakeActionType.Transform, transformed, reason) { OriginalEntity = original };

    // Legacy compatibility methods - using different name to avoid conflicts
    
    /// <summary>
    /// Continue normal processing with the given payload (legacy compatibility).
    /// </summary>
    public static CanonIntakeResult ContinueLegacy(object payload) => new(payload);
    
    /// <summary>
    /// Drop the message entirely - skip all processing (legacy compatibility).
    /// </summary>
    public static CanonIntakeResult DropLegacy(object payload) => new(payload, MustDrop: true);
    
    /// <summary>
    /// Park the message with the specified status for later processing (legacy compatibility).
    /// </summary>
    public static CanonIntakeResult ParkLegacy(object payload, string parkingStatus) => new(payload, ParkingStatus: parkingStatus);
}

/// <summary>
/// Registry for Canon intake interceptors that can modify payloads and return processing instructions.
/// Similar to MessagingInterceptors but designed for Canon intake pipeline with parking capabilities.
/// 
/// DEPRECATED: Use CanonInterceptors.For&lt;T&gt;().BeforeIntake() instead for the new fluent lifecycle API.
/// This class provides backward compatibility but will be removed in v2.0.
/// </summary>
[Obsolete("Use CanonInterceptors.For<T>().BeforeIntake() instead. This API will be removed in v2.0.", false)]
public static class CanonIntakeInterceptors
{
    private static readonly ConcurrentDictionary<Type, Func<object, CanonIntakeResult>> _typeRegistry = new();
    
    /// <summary>
    /// Registers an intake interceptor for a specific type.
    /// The interceptor receives the payload and returns instructions for how to handle it.
    /// 
    /// DEPRECATED: Use CanonInterceptors.For&lt;T&gt;().BeforeIntake() instead.
    /// This method automatically migrates to the new fluent API.
    /// </summary>
    [Obsolete("Use CanonInterceptors.For<T>().BeforeIntake() instead. This method will be removed in v2.0.", false)]
    public static void RegisterForType<T>(Func<T, CanonIntakeResult> interceptor) where T : class
    {
        // Store in legacy registry for backward compatibility
        _typeRegistry[typeof(T)] = obj => interceptor((T)obj);
        
        // Automatically migrate to new fluent API
        var newInterceptor = async (T entity) =>
        {
            var legacyResult = interceptor(entity);
            
            // Convert legacy CanonIntakeResult to new CanonIntakeAction
            if (legacyResult.MustDrop)
                return CanonIntakeActions.Drop(entity, "Legacy interceptor marked for drop");
                
            if (!string.IsNullOrEmpty(legacyResult.ParkingStatus))
                return CanonIntakeActions.Park(entity, legacyResult.ParkingStatus);
                
            return CanonIntakeActions.Continue(entity);
        };
        
        // Register with new fluent API (only if T implements ICanonEntity)
        try
        {
            // Use reflection to call CanonInterceptors.For<T>() if T implements ICanonEntity
            var CanonInterceptorsType = typeof(CanonInterceptors);
            var forMethod = CanonInterceptorsType.GetMethod("For");
            var genericForMethod = forMethod?.MakeGenericMethod(typeof(T));
            var builder = genericForMethod?.Invoke(null, null);
            
            if (builder != null)
            {
                var beforeIntakeMethod = builder.GetType().GetMethod("BeforeIntake", new[] { typeof(Func<T, Task<CanonIntakeAction>>) });
                beforeIntakeMethod?.Invoke(builder, new object[] { newInterceptor });
                
                // Log migration warning
                Console.WriteLine($"[MIGRATION WARNING] CanonIntakeInterceptors.RegisterForType<{typeof(T).Name}> is deprecated. " +
                                $"Use CanonInterceptors.For<{typeof(T).Name}>().BeforeIntake() instead.");
            }
        }
        catch (Exception ex)
        {
            // If migration fails, just log it and continue with legacy behavior
            Console.WriteLine($"[MIGRATION INFO] Could not auto-migrate {typeof(T).Name} to new API: {ex.Message}");
        }
    }

    /// <summary>
    /// Intercepts a payload during Canon intake processing.
    /// Returns processing instructions for the payload.
    /// </summary>
    public static CanonIntakeResult Intercept(object payload)
    {
        var type = payload.GetType();
        if (_typeRegistry.TryGetValue(type, out var interceptor))
        {
            return interceptor(payload);
        }
        return CanonIntakeActions.ContinueLegacy(payload);
    }

    /// <summary>
    /// Checks if an interceptor is registered for the given type.
    /// </summary>
    public static bool HasInterceptor(Type type) => _typeRegistry.ContainsKey(type);
    
    /// <summary>
    /// Checks if an interceptor is registered for the given type.
    /// </summary>
    public static bool HasInterceptor<T>() => _typeRegistry.ContainsKey(typeof(T));
}


