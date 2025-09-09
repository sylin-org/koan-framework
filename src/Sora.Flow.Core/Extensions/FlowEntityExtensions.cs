using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Sora.Core.Json;
using Sora.Flow.Attributes;
using Sora.Flow.Context;
using Sora.Flow.Model;
using Sora.Flow.Core.Messaging;
using Sora.Messaging;
using Sora.Messaging.Contracts;
using System.Dynamic;
using System.Linq;

namespace Sora.Flow.Extensions;

/// <summary>
/// Extension methods for Flow entities that provide clean sending patterns.
/// </summary>
public static class FlowEntityExtensions
{
    /// <summary>
    /// Sends a dictionary as a DynamicFlowEntity of the specified type.
    /// Provides clean DX for sending dynamic data without creating entity instances.
    /// Usage: await myDict.Send&lt;Manufacturer&gt;();
    /// </summary>
    public static async Task Send<T>(this Dictionary<string, object> data, CancellationToken cancellationToken = default) 
        where T : class, IDynamicFlowEntity, new()
    {
        // Convert dictionary to DynamicFlowEntity
        var entity = data.ToDynamicFlowEntity<T>();
        
        // Send using existing messaging infrastructure
        await entity.Send(cancellationToken: cancellationToken);
    }
    
    /// <summary>
    /// Registers MessagingInterceptors for Flow entity types to provide automatic transport envelope wrapping.
    /// This should be called during application startup to configure Flow entity messaging.
    /// </summary>
    public static void RegisterFlowInterceptors()
    {
        Console.WriteLine("[FlowEntityExtensions] DEBUG: Starting FlowEntityExtensions.RegisterFlowInterceptors()");
        
        // Register interceptor for DynamicFlowEntity types using the IDynamicFlowEntity interface
        Console.WriteLine("[FlowEntityExtensions] DEBUG: Registering IDynamicFlowEntity interface interceptor");
        MessagingInterceptors.RegisterForInterface<IDynamicFlowEntity>(entity =>
        {
            Console.WriteLine($"[FlowEntityExtensions] DEBUG: IDynamicFlowEntity interceptor called for {entity.GetType().Name}");
            var envelope = CreateDynamicTransportEnvelope(entity);
            Console.WriteLine($"[FlowEntityExtensions] DEBUG: Creating FlowQueuedMessage for DynamicFlowEntity to route to FlowOrchestrator");
            return new FlowQueuedMessage(envelope); // Route to FlowEntity queue for unified processing
        });
        
        // For regular FlowEntity types, we need to discover and register each type individually
        // since they don't share a common interface
        var allFlowTypes = DiscoverAllFlowTypes();
        Console.WriteLine($"[FlowEntityExtensions] DEBUG: Discovered {allFlowTypes.Count} Flow types");
        
        foreach (var flowType in allFlowTypes)
        {
            Console.WriteLine($"[FlowEntityExtensions] DEBUG: Processing Flow type: {flowType.FullName}");
            var baseType = flowType.BaseType;
            if (baseType != null && baseType.IsGenericType)
            {
                var genericDef = baseType.GetGenericTypeDefinition();
                Console.WriteLine($"[FlowEntityExtensions] DEBUG: Base type: {baseType.FullName}, Generic def: {genericDef.FullName}");
                
                // Skip DynamicFlowEntity types (handled by interface registration above)
                if (genericDef == typeof(DynamicFlowEntity<>))
                {
                    Console.WriteLine($"[FlowEntityExtensions] DEBUG: Skipping DynamicFlowEntity type: {flowType.Name}");
                    continue;
                }
                    
                // Register FlowEntity and FlowValueObject types
                if (genericDef == typeof(FlowEntity<>) || genericDef == typeof(FlowValueObject<>))
                {
                    Console.WriteLine($"[FlowEntityExtensions] DEBUG: Registering FlowEntity/FlowValueObject interceptor for {flowType.Name}");
                    // Use reflection to call RegisterForType<T> with the specific type
                    var method = typeof(MessagingInterceptors).GetMethod("RegisterForType")!.MakeGenericMethod(flowType);
                    var delegateType = typeof(System.Func<,>).MakeGenericType(flowType, typeof(object));
                    var interceptor = System.Delegate.CreateDelegate(delegateType, typeof(FlowEntityExtensions).GetMethod("CreateFlowQueuedMessageGeneric")!.MakeGenericMethod(flowType));
                    method.Invoke(null, new object[] { interceptor });
                    Console.WriteLine($"[FlowEntityExtensions] DEBUG: Successfully registered interceptor for {flowType.Name}");
                }
                else
                {
                    Console.WriteLine($"[FlowEntityExtensions] DEBUG: Skipping non-Flow type: {flowType.Name} (base: {genericDef.FullName})");
                }
            }
            else
            {
                Console.WriteLine($"[FlowEntityExtensions] DEBUG: Skipping type without generic base: {flowType.Name}");
            }
        }
        Console.WriteLine("[FlowEntityExtensions] DEBUG: Completed FlowEntityExtensions.RegisterFlowInterceptors()");
    }
    
    /// <summary>
    /// Generic method for creating transport envelopes for specific FlowEntity types.
    /// Used with reflection to register type-specific interceptors.
    /// </summary>
    public static object CreateTransportEnvelopeGeneric<T>(T entity) where T : class
    {
        return CreateTransportEnvelope(entity);
    }

    /// <summary>
    /// Generic method to create FlowQueuedMessage for any FlowEntity or FlowValueObject type.
    /// Used by reflection in RegisterFlowInterceptors for the new queue routing approach.
    /// </summary>
    /// <typeparam name="T">The entity type</typeparam>
    public static object CreateFlowQueuedMessageGeneric<T>(T entity) where T : class
    {
        Console.WriteLine($"[FlowEntityExtensions] DEBUG: CreateFlowQueuedMessageGeneric called for {typeof(T).Name}");
        var envelope = CreateTransportEnvelope(entity);
        Console.WriteLine($"[FlowEntityExtensions] DEBUG: Created transport envelope, returning FlowQueuedMessage with queue: Sora.Flow.FlowEntity");
        return new FlowQueuedMessage(envelope);
    }
    
    /// <summary>
    /// Creates a transport envelope for regular FlowEntity types.
    /// </summary>
    private static object CreateTransportEnvelope(object entity)
    {
        var entityType = entity.GetType();
        var context = FlowContext.Current ?? GetAdapterContextFromCallStack();
        
        // Create generic transport envelope with strong typing
        var envelopeType = typeof(TransportEnvelope<>).MakeGenericType(entityType);
        var envelope = Activator.CreateInstance(envelopeType)!;
        
        // Set envelope properties via reflection
        SetEnvelopeProperty(envelope, "Version", "1");
        SetEnvelopeProperty(envelope, "Source", context?.GetEffectiveSource());
        SetEnvelopeProperty(envelope, "Model", entityType.Name);
        SetEnvelopeProperty(envelope, "Type", $"TransportEnvelope<{entityType.FullName ?? entityType.Name}>");
        SetEnvelopeProperty(envelope, "Payload", entity);
        SetEnvelopeProperty(envelope, "Timestamp", DateTimeOffset.UtcNow);
        SetEnvelopeProperty(envelope, "Metadata", new Dictionary<string, object?>
        {
            ["system"] = context?.System ?? "unknown",
            ["adapter"] = context?.Adapter ?? "unknown"
        });
        
        // Serialize to JSON string for messaging system
        return envelope.ToJson();
    }
    
    /// <summary>
    /// Creates a dynamic transport envelope for DynamicFlowEntity types.
    /// </summary>
    private static object CreateDynamicTransportEnvelope(IDynamicFlowEntity entity)
    {
        var entityType = entity.GetType();
        var context = FlowContext.Current ?? GetAdapterContextFromCallStack();
        
        // Create DynamicTransportEnvelope<T>
        var envelopeType = typeof(DynamicTransportEnvelope<>).MakeGenericType(entityType);
        var envelope = Activator.CreateInstance(envelopeType)!;
        
        // Extract dictionary payload from DynamicFlowEntity.Model
        var payloadDict = new Dictionary<string, object?>();
        if (entity.Model is ExpandoObject expando)
        {
            payloadDict = FlattenExpandoToDictionary(expando);
        }
        
        // Set envelope properties via reflection
        SetEnvelopeProperty(envelope, "Version", "1");
        SetEnvelopeProperty(envelope, "Source", context?.GetEffectiveSource());
        SetEnvelopeProperty(envelope, "Model", entityType.Name);
        SetEnvelopeProperty(envelope, "Type", $"DynamicTransportEnvelope<{entityType.FullName ?? entityType.Name}>");
        SetEnvelopeProperty(envelope, "Payload", payloadDict);
        SetEnvelopeProperty(envelope, "Timestamp", DateTimeOffset.UtcNow);
        SetEnvelopeProperty(envelope, "Metadata", new Dictionary<string, object?>
        {
            ["system"] = context?.System ?? "unknown",
            ["adapter"] = context?.Adapter ?? "unknown"
        });
        
        // Serialize to JSON string for messaging system
        return envelope.ToJson();
    }
    
    /// <summary>
    /// Flattens an ExpandoObject to a dictionary with JSON path keys.
    /// </summary>
    private static Dictionary<string, object?> FlattenExpandoToDictionary(ExpandoObject expando, string prefix = "")
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var dict = (IDictionary<string, object?>)expando;
        
        foreach (var kvp in dict)
        {
            var currentPath = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}.{kvp.Key}";
            
            if (kvp.Value is ExpandoObject nested)
            {
                // Recursively flatten nested ExpandoObjects
                var nestedFlattened = FlattenExpandoToDictionary(nested, currentPath);
                foreach (var nestedKvp in nestedFlattened)
                {
                    result[nestedKvp.Key] = nestedKvp.Value;
                }
            }
            else if (kvp.Value != null)
            {
                result[currentPath] = kvp.Value;
            }
        }
        
        return result;
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
    
    /// <summary>
    /// Discovers all Flow entity types across all loaded assemblies.
    /// Returns FlowEntity&lt;T&gt;, DynamicFlowEntity&lt;T&gt;, and FlowValueObject&lt;T&gt; types.
    /// </summary>
    private static List<Type> DiscoverAllFlowTypes()
    {
        var result = new List<Type>();
        
        // Scan all assemblies in the current AppDomain
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        
        foreach (var assembly in assemblies)
        {
            var flowTypes = DiscoverFlowTypesInAssembly(assembly);
            result.AddRange(flowTypes);
        }
        
        return result;
    }
    
    /// <summary>
    /// Discovers Flow entity types within a specific assembly.
    /// </summary>
    private static List<Type> DiscoverFlowTypesInAssembly(Assembly assembly)
    {
        var result = new List<Type>();
        
        try
        {
            Type?[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
            catch { return result; }
            
            foreach (var t in types)
            {
                if (t is null || !t.IsClass || t.IsAbstract) continue;
                
                // Check for FlowIgnore attribute to opt out
                if (t.GetCustomAttribute<FlowIgnoreAttribute>() is not null) continue;
                
                var bt = t.BaseType;
                if (bt is null || !bt.IsGenericType) continue;
                
                var def = bt.GetGenericTypeDefinition();
                
                // Include all Flow entity types
                if (def == typeof(FlowEntity<>) || 
                    def == typeof(FlowValueObject<>) || 
                    def == typeof(DynamicFlowEntity<>))
                {
                    result.Add(t);
                }
            }
        }
        catch (Exception ex)
        {
            // Log error but don't fail - some assemblies might not be accessible
            Console.WriteLine($"[FlowEntityExtensions] Warning: Failed to scan assembly {assembly.FullName}: {ex.Message}");
        }
        
        return result;
    }
}

