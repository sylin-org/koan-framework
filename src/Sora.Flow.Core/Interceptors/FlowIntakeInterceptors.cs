using System;
using System.Collections.Concurrent;

namespace Sora.Flow.Core.Interceptors;

/// <summary>
/// Result from a Flow intake interceptor indicating how to handle the payload.
/// </summary>
public record FlowIntakeResult(
    object Payload,
    bool MustDrop = false,
    string? ParkingStatus = null
);

/// <summary>
/// Helper methods for creating FlowIntakeResult instances.
/// </summary>
public static class FlowIntakeActions
{
    /// <summary>
    /// Continue normal processing with the given payload.
    /// </summary>
    public static FlowIntakeResult Continue(object payload) => new(payload);
    
    /// <summary>
    /// Drop the message entirely - skip all processing.
    /// </summary>
    public static FlowIntakeResult Drop(object payload) => new(payload, MustDrop: true);
    
    /// <summary>
    /// Park the message with the specified status for later processing.
    /// </summary>
    public static FlowIntakeResult Park(object payload, string parkingStatus) => new(payload, ParkingStatus: parkingStatus);
}

/// <summary>
/// Registry for Flow intake interceptors that can modify payloads and return processing instructions.
/// Similar to MessagingInterceptors but designed for Flow intake pipeline with parking capabilities.
/// </summary>
public static class FlowIntakeInterceptors
{
    private static readonly ConcurrentDictionary<Type, Func<object, FlowIntakeResult>> _typeRegistry = new();
    
    /// <summary>
    /// Registers an intake interceptor for a specific type.
    /// The interceptor receives the payload and returns instructions for how to handle it.
    /// </summary>
    public static void RegisterForType<T>(Func<T, FlowIntakeResult> interceptor)
        => _typeRegistry[typeof(T)] = obj => interceptor((T)obj);

    /// <summary>
    /// Intercepts a payload during Flow intake processing.
    /// Returns processing instructions for the payload.
    /// </summary>
    public static FlowIntakeResult Intercept(object payload)
    {
        var type = payload.GetType();
        if (_typeRegistry.TryGetValue(type, out var interceptor))
        {
            return interceptor(payload);
        }
        return FlowIntakeActions.Continue(payload);
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