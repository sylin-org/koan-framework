using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Sora.Core.Json;
using Sora.Flow.Attributes;
using Sora.Flow.Context;
using Sora.Flow.Model;
using Sora.Messaging;

namespace Sora.Flow.Extensions;

/// <summary>
/// Extension methods for Flow entities that provide clean sending patterns.
/// </summary>
public static class FlowEntityExtensions
{
    /// <summary>
    /// Sends any Flow entity (FlowEntity, DynamicFlowEntity, or FlowValueObject) through the messaging system 
    /// with automatic transport envelope wrapping and JSON serialization.
    /// </summary>
    /// <param name="entity">The entity to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the send operation</returns>
    public static async Task Send(this object entity, CancellationToken cancellationToken = default)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        
        System.Diagnostics.Debug.WriteLine($"[FlowEntityExtensions] DEBUG: Send() called for entity type {entity.GetType().Name}");
        Console.Error.WriteLine($"[FlowEntityExtensions] DEBUG: Send() called for entity type {entity.GetType().Name}");
        
        // Verify this is a Flow entity type
        var entityType = entity.GetType();
        if (!IsFlowEntityType(entityType))
        {
            throw new ArgumentException($"Entity type {entityType.Name} is not a Flow entity type. Must inherit from FlowEntity<T>, DynamicFlowEntity<T>, or FlowValueObject<T>.");
        }
        
        // Get current flow context for adapter identity
        var context = FlowContext.Current ?? GetAdapterContextFromCallStack();
        Console.Error.WriteLine($"[FlowEntityExtensions] DEBUG: Context - System: {context?.System}, Adapter: {context?.Adapter}");
        
        // Create generic transport envelope with strong typing
        var envelopeType = typeof(TransportEnvelope<>).MakeGenericType(entityType);
        var envelope = Activator.CreateInstance(envelopeType);
        Console.Error.WriteLine($"[FlowEntityExtensions] DEBUG: Created envelope type: {envelopeType.Name}");
        
        // Set envelope properties via reflection (since we're creating generic type dynamically)
        SetEnvelopeProperty(envelope, "Version", "1");
        SetEnvelopeProperty(envelope, "Source", context?.GetEffectiveSource());
        SetEnvelopeProperty(envelope, "Model", entityType.Name);
        var typeString = $"TransportEnvelope<{entityType.FullName ?? entityType.Name}>";
        SetEnvelopeProperty(envelope, "Type", typeString);
        SetEnvelopeProperty(envelope, "Payload", entity);
        SetEnvelopeProperty(envelope, "Timestamp", DateTimeOffset.UtcNow);
        SetEnvelopeProperty(envelope, "Metadata", new Dictionary<string, object?>
        {
            ["system"] = context?.System ?? "unknown",
            ["adapter"] = context?.Adapter ?? "unknown"
        });
        
        Console.Error.WriteLine($"[FlowEntityExtensions] DEBUG: Envelope configured - Type: {typeString}");
        
        // Serialize to JSON using Sora.Core (eliminates JsonElements)
        var json = envelope.ToJson();
        Console.Error.WriteLine($"[FlowEntityExtensions] DEBUG: JSON serialized, length: {json.Length}, starts with: {json.Substring(0, Math.Min(100, json.Length))}...");
        
        // Send JSON string via messaging system
        await MessagingExtensions.Send(json, cancellationToken: cancellationToken);
        Console.Error.WriteLine($"[FlowEntityExtensions] DEBUG: JSON string sent to messaging system");
    }
    
    /// <summary>
    /// Helper method to set properties on dynamically created generic envelope.
    /// </summary>
    private static void SetEnvelopeProperty(object envelope, string propertyName, object? value)
    {
        var property = envelope.GetType().GetProperty(propertyName);
        property?.SetValue(envelope, value);
    }
    
    /// <summary>
    /// Attempts to determine the adapter context by examining the call stack for [FlowAdapter] attributes.
    /// This is a fallback when FlowContext.Current is not set.
    /// </summary>
    private static FlowContext? GetAdapterContextFromCallStack()
    {
        try
        {
            var stackTrace = new System.Diagnostics.StackTrace();
            var frames = stackTrace.GetFrames();
            
            if (frames == null) return null;
            
            foreach (var frame in frames)
            {
                var method = frame.GetMethod();
                if (method?.DeclaringType == null) continue;
                
                var adapterAttr = method.DeclaringType.GetCustomAttribute<FlowAdapterAttribute>(inherit: true);
                if (adapterAttr != null)
                {
                    return new FlowContext(adapterAttr.System, adapterAttr.Adapter, adapterAttr.DefaultSource);
                }
            }
        }
        catch
        {
            // Stack trace analysis failed - return null to fall back to "unknown"
        }
        
        return null;
    }
    
    /// <summary>
    /// Checks if a type is a Flow entity type (FlowEntity, DynamicFlowEntity, or FlowValueObject).
    /// </summary>
    private static bool IsFlowEntityType(Type type)
    {
        if (type == null || !type.IsClass || type.IsAbstract) return false;
        
        var baseType = type.BaseType;
        if (baseType == null || !baseType.IsGenericType) return false;
        
        var genericDef = baseType.GetGenericTypeDefinition();
        return genericDef == typeof(FlowEntity<>) || 
               genericDef == typeof(DynamicFlowEntity<>) || 
               genericDef == typeof(FlowValueObject<>);
    }
}