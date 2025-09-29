using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Json;
using Koan.Canon.Attributes;
using Koan.Canon.Context;
using Koan.Canon.Model;
using Koan.Canon.Core.Messaging;
using Koan.Messaging;
using Koan.Messaging.Contracts;
using System.Dynamic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Koan.Canon.Extensions;

/// <summary>
/// Extension methods for Canon entities that provide clean sending patterns.
/// </summary>
public static class CanonEntityExtensions
{
    /// <summary>
    /// Sends a dictionary as a DynamicCanonEntity of the specified type.
    /// Provides clean DX for sending dynamic data without creating entity instances.
    /// Usage: await myDict.Send&lt;Manufacturer&gt;();
    /// </summary>
    public static async Task Send<T>(this Dictionary<string, object> data, CancellationToken cancellationToken = default)
        where T : class, IDynamicCanonEntity, new()
    {
        // Convert dictionary to DynamicCanonEntity
        var entity = data.ToDynamicCanonEntity<T>();

        // Send using existing messaging infrastructure
        await entity.Send(cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Registers MessagingInterceptors for Canon entity types to provide automatic transport envelope wrapping.
    /// This should be called during application startup to configure Canon entity messaging.
    /// </summary>
    public static void RegisterCanonInterceptors()
    {

        // Register interceptor for DynamicCanonEntity types using the IDynamicCanonEntity interface
        MessagingInterceptors.RegisterForInterface<IDynamicCanonEntity>(entity =>
        {
            var envelope = CreateDynamicTransportEnvelope(entity);
            return new CanonQueuedMessage(envelope); // Route to CanonEntity queue for unified processing
        });

        // For regular CanonEntity types, we need to discover and register each type individually
        // since they don't share a common interface
        var allCanonTypes = DiscoverAllCanonTypes();

        foreach (var CanonType in allCanonTypes)
        {
            var baseType = CanonType.BaseType;
            if (baseType != null && baseType.IsGenericType)
            {
                var genericDef = baseType.GetGenericTypeDefinition();

                // Skip DynamicCanonEntity types (handled by interface registration above)
                if (genericDef == typeof(DynamicCanonEntity<>))
                {
                    continue;
                }

                // Register CanonEntity and CanonValueObject types
                if (genericDef == typeof(CanonEntity<>) || genericDef == typeof(CanonValueObject<>))
                {
                    // Use reflection to call RegisterForType<T> with the specific type
                    var method = typeof(MessagingInterceptors).GetMethod("RegisterForType")!.MakeGenericMethod(CanonType);
                    var delegateType = typeof(System.Func<,>).MakeGenericType(CanonType, typeof(object));
                    var interceptor = System.Delegate.CreateDelegate(delegateType, typeof(CanonEntityExtensions).GetMethod("CreateCanonQueuedMessageGeneric")!.MakeGenericMethod(CanonType));
                    method.Invoke(null, new object[] { interceptor });
                }
                else
                {
                }
            }
            else
            {
            }
        }
    }

    /// <summary>
    /// Generic method for creating transport envelopes for specific CanonEntity types.
    /// Used with reflection to register type-specific interceptors.
    /// </summary>
    public static object CreateTransportEnvelopeGeneric<T>(T entity) where T : class
    {
        return CreateTransportEnvelope(entity);
    }

    /// <summary>
    /// Generic method to create CanonQueuedMessage for any CanonEntity or CanonValueObject type.
    /// Used by reflection in RegisterCanonInterceptors for the new queue routing approach.
    /// </summary>
    /// <typeparam name="T">The entity type</typeparam>
    public static object CreateCanonQueuedMessageGeneric<T>(T entity) where T : class
    {
        var envelope = CreateTransportEnvelope(entity);
        return new CanonQueuedMessage(envelope);
    }

    /// <summary>
    /// Creates a transport envelope for regular CanonEntity types.
    /// </summary>
    private static object CreateTransportEnvelope(object entity)
    {
        var entityType = entity.GetType();
        var context = CanonContext.Current ?? GetAdapterContextFromCallStack();

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
    /// Creates a dynamic transport envelope for DynamicCanonEntity types.
    /// </summary>
    private static object CreateDynamicTransportEnvelope(IDynamicCanonEntity entity)
    {
        var entityType = entity.GetType();
        var context = CanonContext.Current ?? GetAdapterContextFromCallStack();

        // Create DynamicTransportEnvelope<T>
        var envelopeType = typeof(DynamicTransportEnvelope<>).MakeGenericType(entityType);
        var envelope = Activator.CreateInstance(envelopeType)!;

        // Extract dictionary payload from DynamicCanonEntity.Model
        var payloadDict = new Dictionary<string, object?>();
        if (entity.Model is JObject jObject)
        {
            payloadDict = FlattenJObjectToDictionary(jObject);
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
    /// Flattens a JObject to a dictionary with JSON path keys.
    /// </summary>
    private static Dictionary<string, object?> FlattenJObjectToDictionary(JObject jObject, string prefix = "")
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in jObject.Properties())
        {
            var currentPath = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";

            if (property.Value is JObject nested)
            {
                var nestedFlattened = FlattenJObjectToDictionary(nested, currentPath);
                foreach (var nestedKvp in nestedFlattened)
                {
                    result[nestedKvp.Key] = nestedKvp.Value;
                }
            }
            else if (property.Value.Type != Newtonsoft.Json.Linq.JTokenType.Null)
            {
                result[currentPath] = property.Value.ToObject<object>();
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
    /// Attempts to determine the adapter context by examining the call stack for [CanonAdapter] attributes.
    /// This is a fallback when CanonContext.Current is not set.
    /// </summary>
    private static CanonContext? GetAdapterContextFromCallStack()
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

                var adapterAttr = method.DeclaringType.GetCustomAttribute<CanonAdapterAttribute>(inherit: true);
                if (adapterAttr != null)
                {
                    return new CanonContext(adapterAttr.System, adapterAttr.Adapter, adapterAttr.DefaultSource);
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
    /// Checks if a type is a Canon entity type (CanonEntity, DynamicCanonEntity, or CanonValueObject).
    /// </summary>
    private static bool IsCanonEntityType(Type type)
    {
        if (type == null || !type.IsClass || type.IsAbstract) return false;

        var baseType = type.BaseType;
        if (baseType == null || !baseType.IsGenericType) return false;

        var genericDef = baseType.GetGenericTypeDefinition();
        return genericDef == typeof(CanonEntity<>) ||
               genericDef == typeof(DynamicCanonEntity<>) ||
               genericDef == typeof(CanonValueObject<>);
    }

    /// <summary>
    /// Discovers all Canon entity types across all loaded assemblies.
    /// Returns CanonEntity&lt;T&gt;, DynamicCanonEntity&lt;T&gt;, and CanonValueObject&lt;T&gt; types.
    /// </summary>
    private static List<Type> DiscoverAllCanonTypes()
    {
        var result = new List<Type>();

        // Scan all assemblies in the current AppDomain
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            var CanonTypes = DiscoverCanonTypesInAssembly(assembly);
            result.AddRange(CanonTypes);
        }

        return result;
    }

    /// <summary>
    /// Discovers Canon entity types within a specific assembly.
    /// </summary>
    private static List<Type> DiscoverCanonTypesInAssembly(Assembly assembly)
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

                // Check for CanonIgnore attribute to opt out
                if (t.GetCustomAttribute<CanonIgnoreAttribute>() is not null) continue;

                var bt = t.BaseType;
                if (bt is null || !bt.IsGenericType) continue;

                var def = bt.GetGenericTypeDefinition();

                // Include all Canon entity types
                if (def == typeof(CanonEntity<>) ||
                    def == typeof(CanonValueObject<>) ||
                    def == typeof(DynamicCanonEntity<>))
                {
                    result.Add(t);
                }
            }
        }
        catch (Exception ex)
        {
            // Log error but don't fail - some assemblies might not be accessible
        }

        return result;
    }
}




