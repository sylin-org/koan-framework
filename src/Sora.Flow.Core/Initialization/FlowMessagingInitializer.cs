using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sora.Core.Json;
using Sora.Flow.Attributes;
using Sora.Flow.Model;
using Sora.Messaging;

namespace Sora.Flow.Initialization;

/// <summary>
/// Initializes Flow messaging by automatically registering transport envelope handlers
/// for all discovered Flow entity types across all loaded assemblies.
/// </summary>
public static class FlowMessagingInitializer
{
    /// <summary>
    /// Registers transport envelope transformers for all Flow entity types found in loaded assemblies.
    /// This eliminates the need for manual registration and ensures all entities can use entity.Send().
    /// </summary>
    public static void RegisterFlowTransformers()
    {
        var allFlowTypes = DiscoverAllFlowTypes();
        
        foreach (var flowType in allFlowTypes)
        {
            // Register each entity type so MessagingTransformers can wrap it in TransportEnvelope
            // The transformer key is the full type name for precise matching
            MessagingTransformers.Register(flowType.FullName ?? flowType.Name, payload =>
            {
                // Note: This transformer is called when the entity is sent
                // The actual FlowContext will be captured in the Send extension method
                // This is just a passthrough to let the messaging system know
                // that this type should be handled specially
                return payload;
            });
        }
    }
    
    /// <summary>
    /// Adds the Flow transport handler to the service collection.
    /// This handler processes JSON strings from the "sora.flow:transport" category.
    /// </summary>
    public static IServiceCollection AddFlowTransportHandler(this IServiceCollection services)
    {
        // Register message handler for JSON strings (will filter for Flow transport)
        services.On<string>(async json =>
        {
            Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Received JSON string, length: {json.Length}");
            Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: JSON preview: {json.Substring(0, Math.Min(200, json.Length))}...");
            
            // Check if this is a Flow transport envelope by looking for our Type field
            if (json.Contains("\"type\":\"TransportEnvelope<"))
            {
                Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Identified as Flow transport envelope, processing...");
                var handler = services.BuildServiceProvider().GetService<FlowTransportProcessor>();
                if (handler != null)
                {
                    await handler.ProcessFlowTransportJson(json);
                }
                else
                {
                    Console.Error.WriteLine($"[FlowMessagingInitializer] ERROR: FlowTransportProcessor not found in services");
                }
            }
            else
            {
                Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Not a Flow transport envelope, ignoring");
            }
        });
        
        // Register the processor as a service
        services.AddSingleton<FlowTransportProcessor>();
        return services;
    }
    
    /// <summary>
    /// Discovers all Flow entity types across all loaded assemblies.
    /// Returns FlowEntity<T>, DynamicFlowEntity<T>, and FlowValueObject<T> types.
    /// </summary>
    public static List<Type> DiscoverAllFlowTypes()
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
    /// Based on the pattern from SoraAutoRegistrar.
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
            Console.WriteLine($"[FlowMessagingInitializer] Warning: Failed to scan assembly {assembly.FullName}: {ex.Message}");
        }
        
        return result;
    }
}

/// <summary>
/// Service that handles incoming Flow transport JSON strings and processes them into Flow intake.
/// Uses JSON path querying and type caching for optimal performance.
/// </summary>
internal class FlowTransportProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FlowTransportProcessor> _logger;
    private static readonly Dictionary<string, Type> _envelopeTypeCache = new();
    private static readonly object _cacheLock = new();
    
    public FlowTransportProcessor(IServiceProvider serviceProvider, ILogger<FlowTransportProcessor> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }
    
    public async Task ProcessFlowTransportJson(string json)
    {
        try
        {
            Console.Error.WriteLine($"[FlowTransportProcessor] DEBUG: Starting ProcessFlowTransportJson, JSON length: {json.Length}");
            
            // Parse JSON and extract type information using Newtonsoft paths
            var jObject = Newtonsoft.Json.Linq.JObject.Parse(json);
            var typeValue = jObject["type"]?.ToString();
            var modelValue = jObject["model"]?.ToString();
            var systemValue = jObject["metadata"]?["system"]?.ToString();
            var adapterValue = jObject["metadata"]?["adapter"]?.ToString();
            
            Console.Error.WriteLine($"[FlowTransportProcessor] DEBUG: Extracted - Type: {typeValue}, Model: {modelValue}, System: {systemValue}, Adapter: {adapterValue}");
            
            _logger.LogDebug("[FlowTransport] Processing JSON transport: Model={Model}, System={System}, Adapter={Adapter}", 
                modelValue, systemValue, adapterValue);
            
            if (string.IsNullOrEmpty(typeValue))
            {
                _logger.LogWarning("[FlowTransport] Missing Type field in transport JSON");
                return;
            }
            
            // Extract payload type from generic envelope type (with caching)
            var envelopeType = GetCachedEnvelopeType(typeValue);
            if (envelopeType == null)
            {
                _logger.LogWarning("[FlowTransport] Could not resolve envelope type: {Type}", typeValue);
                return;
            }
            
            // Deserialize entire envelope using strongly-typed generic
            var envelope = json.FromJson(envelopeType);
            if (envelope == null)
            {
                _logger.LogWarning("[FlowTransport] Failed to deserialize transport envelope");
                return;
            }
            
            // Extract payload and metadata using reflection (since envelope is dynamic type)
            var payloadProperty = envelopeType.GetProperty("Payload");
            var metadataProperty = envelopeType.GetProperty("Metadata");
            var sourceProperty = envelopeType.GetProperty("Source");
            var timestampProperty = envelopeType.GetProperty("Timestamp");
            
            var entity = payloadProperty?.GetValue(envelope);
            var metadata = metadataProperty?.GetValue(envelope) as Dictionary<string, object?>;
            var source = sourceProperty?.GetValue(envelope)?.ToString();
            var timestamp = (DateTimeOffset)(timestampProperty?.GetValue(envelope) ?? DateTimeOffset.UtcNow);
            
            if (entity == null)
            {
                _logger.LogWarning("[FlowTransport] Null payload in envelope for model: {Model}", modelValue);
                return;
            }
            
            // Get payload type for Flow processing
            var payloadType = entity.GetType();
            
            // Get the Flow sender service
            var sender = _serviceProvider.GetService<Sending.IFlowSender>();
            if (sender == null)
            {
                _logger.LogError("[FlowTransport] IFlowSender not available");
                return;
            }
            
            // Convert entity to bag dictionary
            var bag = ExtractEntityBag(entity);
            
            // Add metadata to bag (no JsonElements since we used Newtonsoft.Json throughout)
            if (metadata != null)
            {
                foreach (var kvp in metadata)
                {
                    bag[kvp.Key] = kvp.Value;
                }
            }
            
            // Create the flow send item with preserved source information
            var sourceId = source ?? "transport-handler";
            var item = new Sending.FlowSendPlainItem(payloadType, sourceId, timestamp, bag);
            
            // Send to Flow intake
            await sender.SendAsync(new[] { item }, null, null, null);
            
            _logger.LogDebug("[FlowTransport] Successfully processed transport for model: {Model}", modelValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FlowTransport] Failed to process transport JSON: {Error}", ex.Message);
        }
    }
    
    /// <summary>
    /// Gets or creates cached envelope type from type string with performance optimization.
    /// </summary>
    private static Type? GetCachedEnvelopeType(string typeValue)
    {
        if (_envelopeTypeCache.TryGetValue(typeValue, out var cachedType))
        {
            return cachedType;
        }
        
        lock (_cacheLock)
        {
            // Double-check pattern
            if (_envelopeTypeCache.TryGetValue(typeValue, out cachedType))
            {
                return cachedType;
            }
            
            // Parse type string: "TransportEnvelope<S8.Flow.Shared.Device>"
            var match = System.Text.RegularExpressions.Regex.Match(typeValue, @"TransportEnvelope<(.+)>");
            if (!match.Success)
            {
                return null;
            }
            
            var payloadTypeName = match.Groups[1].Value;
            var payloadType = FindTypeInAssemblies(payloadTypeName);
            if (payloadType == null)
            {
                return null;
            }
            
            // Create generic envelope type
            var envelopeType = typeof(TransportEnvelope<>).MakeGenericType(payloadType);
            _envelopeTypeCache[typeValue] = envelopeType;
            
            return envelopeType;
        }
    }
    
    /// <summary>
    /// Extracts entity properties into a bag dictionary, handling both regular and dynamic entities.
    /// This is similar to the ToBag method from auto-handlers but with better DynamicFlowEntity support.
    /// </summary>
    private static Dictionary<string, object?> ExtractEntityBag(object entity)
    {
        var bag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        
        if (entity == null) return bag;
        
        try
        {
            // Special handling for DynamicFlowEntity - extract from Model property
            if (entity is IDynamicFlowEntity dynamicEntity && dynamicEntity.Model != null)
            {
                // Flatten the ExpandoObject Model to dictionary paths
                var flattened = FlattenExpando(dynamicEntity.Model, "");
                foreach (var kvp in flattened)
                {
                    bag[kvp.Key] = ConvertJsonElementValue(kvp.Value);
                }
                
                // Also add the Id if present
                var idProp = entity.GetType().GetProperty("Id");
                if (idProp != null && idProp.CanRead)
                {
                    var idVal = idProp.GetValue(entity);
                    if (idVal != null) bag["Id"] = ConvertJsonElementValue(idVal);
                }
            }
            else
            {
                // Regular entity - extract simple properties
                var props = entity.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
                foreach (var prop in props)
                {
                    if (!prop.CanRead) continue;
                    var val = prop.GetValue(entity);
                    if (val == null || IsSimpleType(val.GetType()))
                    {
                        bag[prop.Name] = ConvertJsonElementValue(val);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FlowTransport] Warning: Failed to extract entity bag: {ex.Message}");
        }
        
        return bag;
    }
    
    /// <summary>
    /// Flattens an ExpandoObject to dotted path notation for aggregation key matching.
    /// </summary>
    private static Dictionary<string, object?> FlattenExpando(System.Dynamic.ExpandoObject expando, string prefix)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var dict = (IDictionary<string, object?>)expando;
        
        foreach (var kvp in dict)
        {
            var currentPath = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}.{kvp.Key}";
            
            if (kvp.Value is System.Dynamic.ExpandoObject nested)
            {
                var nestedFlattened = FlattenExpando(nested, currentPath);
                foreach (var nestedKvp in nestedFlattened)
                {
                    result[nestedKvp.Key] = nestedKvp.Value;
                }
            }
            else
            {
                result[currentPath] = ConvertJsonElementValue(kvp.Value);
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Checks if a type is a simple type that can be serialized directly.
    /// </summary>
    private static bool IsSimpleType(Type type)
    {
        if (type.IsPrimitive || type.IsEnum) return true;
        return type == typeof(string) || 
               type == typeof(decimal) || 
               type == typeof(DateTime) || 
               type == typeof(DateTimeOffset) || 
               type == typeof(Guid) || 
               type == typeof(TimeSpan);
    }
    
    /// <summary>
    /// Converts JsonElement values to primitive types that can be serialized to MongoDB.
    /// Handles the common case where RabbitMQ deserializes metadata as JsonElement objects.
    /// </summary>
    private static object? ConvertJsonElementValue(object? value)
    {
        if (value == null) return null;
        
        // Handle System.Text.Json.JsonElement
        if (value is System.Text.Json.JsonElement jsonElement)
        {
            return jsonElement.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => jsonElement.GetString(),
                System.Text.Json.JsonValueKind.Number => jsonElement.TryGetInt64(out var longVal) ? longVal : jsonElement.GetDouble(),
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Null => null,
                _ => jsonElement.ToString() // For objects/arrays, convert to string
            };
        }
        
        // Return as-is for other types
        return value;
    }
    
    /// <summary>
    /// Finds a type by name in all loaded assemblies.
    /// </summary>
    private static Type? FindTypeInAssemblies(string typeName)
    {
        // First try the simple approach
        var type = Type.GetType(typeName);
        if (type != null) return type;
        
        // Search through all loaded assemblies
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                type = assembly.GetType(typeName);
                if (type != null) return type;
            }
            catch
            {
                // Assembly might not be accessible, skip
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Deserializes the payload to the exact expected type using Sora.Core JSON capabilities.
    /// This eliminates JsonElement contamination by using Newtonsoft.Json instead of System.Text.Json.
    /// </summary>
    private static object? DeserializePayloadToExactType(object? payload, Type targetType)
    {
        if (payload == null) return null;
        
        try
        {
            // Use Sora.Core JSON round-trip to eliminate JsonElements
            // Newtonsoft.Json doesn't create JsonElement objects like System.Text.Json does
            var json = payload.ToJson();
            var cleanEntity = json.FromJson(targetType);
            
            return cleanEntity;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FlowTransport] Failed to deserialize payload to type {targetType.Name}: {ex.Message}");
            return null;
        }
    }
}