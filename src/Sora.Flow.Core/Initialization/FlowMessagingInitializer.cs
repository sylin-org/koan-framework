using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sora.Core;
using Sora.Core.Hosting.App;
using Sora.Core.Json;
using Sora.Flow.Actions;
using Sora.Flow.Attributes;
using Sora.Flow.Context;
using Sora.Flow.Model;
using Sora.Flow.Infrastructure;
using Sora.Data.Core;
using Sora.Messaging;

namespace Sora.Flow.Initialization;

/// <summary>
/// Initializes Flow messaging by automatically registering transport envelope handlers
/// for all discovered Flow entity types across all loaded assemblies.
/// </summary>
public static class FlowMessagingInitializer
{
    /// <summary>
    /// Helper method to set properties on dynamically created envelopes.
    /// </summary>
    private static void SetEnvelopeProperty(object envelope, string propertyName, object? value)
    {
        var property = envelope.GetType().GetProperty(propertyName);
        property?.SetValue(envelope, value);
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
    /// Attempts to get adapter context from call stack when FlowContext.Current is not set.
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
            // Stack trace analysis failed - return null
        }
        
        return null;
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
            if (json.Contains("\"type\":\"TransportEnvelope<") || json.Contains("\"type\":\"DynamicTransportEnvelope<"))
            {
                Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Flow transport envelope - processing into Flow pipeline");
                await ProcessFlowTransportEnvelope(json);
            }
            else
            {
                Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Not a Flow transport envelope, ignoring");
            }
        });
        
        return services;
    }
    
    /// <summary>
    /// Processes a Flow transport envelope into the Flow pipeline stages.
    /// Handles both regular TransportEnvelope and DynamicTransportEnvelope types.
    /// </summary>
    private static async Task ProcessFlowTransportEnvelope(string json)
    {
        try
        {
            // Deserialize the envelope as a dynamic object to extract metadata and payload
            dynamic envelope = Newtonsoft.Json.JsonConvert.DeserializeObject(json)!;
            
            string model = envelope.model;
            string source = envelope.source;
            object payload = envelope.payload;
            string? correlationId = envelope.metadata?.correlation_id;
            
            Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Processing {model} from {source}");
            
            // Get the IFlowActions service from the current service provider
            var serviceProvider = AppHost.Current;
            if (serviceProvider == null)
            {
                Console.Error.WriteLine($"[FlowMessagingInitializer] ERROR: AppHost.Current is null, cannot process Flow envelope");
                return;
            }
            
            var flowActions = serviceProvider.GetService<IFlowActions>();
            if (flowActions == null)
            {
                Console.Error.WriteLine($"[FlowMessagingInitializer] ERROR: IFlowActions service not registered");
                return;
            }
            
            // Generate a reference ID based on the payload
            string referenceId = GenerateReferenceId(payload);
            
            Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Seeding {model} with referenceId {referenceId} - bypassing FlowActions");
            
            // BYPASS FLOWACTIONS - Direct Flow pipeline integration
            // Instead of calling FlowActions.SeedAsync() which creates FlowAction messages,
            // directly invoke the FlowActionHandler logic to persist to MongoDB
            try
            {
                var modelType = Sora.Flow.Infrastructure.FlowRegistry.ResolveModel(model);
                if (modelType != null)
                {
                    Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Resolved model type {modelType.Name} for {model}");
                    await DirectSeedToIntake(modelType, model, referenceId, payload, source, correlationId);
                    Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Successfully seeded {model} directly to intake");
                }
                else
                {
                    Console.Error.WriteLine($"[FlowMessagingInitializer] ERROR: Could not resolve model type for {model}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[FlowMessagingInitializer] ERROR: Failed to seed {model} directly: {ex.Message}");
                // Fallback to original FlowActions approach
                await flowActions.SeedAsync(
                    model: model,
                    referenceId: referenceId,
                    payload: payload,
                    correlationId: correlationId
                );
            }
            
            Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Successfully seeded {model} into Flow pipeline");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FlowMessagingInitializer] ERROR: Failed to process Flow transport envelope: {ex.Message}");
            Console.Error.WriteLine($"[FlowMessagingInitializer] ERROR: Stack trace: {ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// Generates a reference ID for the Flow entity based on its payload.
    /// For entities with an Id property, use that. Otherwise generate a new ULID.
    /// </summary>
    private static string GenerateReferenceId(object payload)
    {
        try
        {
            // Try to extract Id from payload if it's a dynamic object
            if (payload is JObject jObject)
            {
                var id = jObject["id"]?.ToString() ?? jObject["Id"]?.ToString();
                if (!string.IsNullOrEmpty(id))
                {
                    return id;
                }
                
                // For DynamicFlowEntity, try to get identifier fields from nested model
                if (jObject["model"] is JObject modelObj)
                {
                    // Try common identifier patterns for dynamic entities
                    var code = modelObj["identifier"]?["code"]?.ToString();
                    if (!string.IsNullOrEmpty(code))
                    {
                        return code;
                    }
                }
            }
        }
        catch
        {
            // Fall through to generate new ULID
        }
        
        // Generate a new GUID as fallback
        return StringId.New();
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
    
    /// <summary>
    /// Directly seeds payload into Flow intake stage, bypassing FlowAction messaging.
    /// Replicates FlowActionHandler.HandleSeedAsync logic.
    /// </summary>
    private static async Task DirectSeedToIntake(Type modelType, string model, string referenceId, object payload, string source, string? correlationId)
    {
        // Create StageRecord<TModel> and save to intake set (from FlowActionHandler.HandleSeedAsync)
        var recordType = typeof(StageRecord<>).MakeGenericType(modelType);
        var record = Activator.CreateInstance(recordType)!;
        recordType.GetProperty("Id")!.SetValue(record, Guid.NewGuid().ToString("n"));
        recordType.GetProperty("SourceId")!.SetValue(record, referenceId ?? model);
        recordType.GetProperty("OccurredAt")!.SetValue(record, DateTimeOffset.UtcNow);
        
        // Convert payload to dictionary format (pure business data)
        var data = ToDict(payload);
        recordType.GetProperty("Data")!.SetValue(record, data);
        
        // Create separate source metadata dictionary
        var sourceMetadata = new Dictionary<string, object?>
        {
            [Constants.Envelope.System] = source,
            [Constants.Envelope.Adapter] = source
        };
        recordType.GetProperty("Source")!.SetValue(record, sourceMetadata);

        // Save to MongoDB using Data<,>.UpsertAsync (from FlowActionHandler.HandleSeedAsync)
        var dataType = typeof(Data<,>).MakeGenericType(recordType, typeof(string));
        var upsert = dataType.GetMethod("UpsertAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, new[] { recordType, typeof(string), typeof(CancellationToken) })!;
        await (Task)upsert.Invoke(null, new object?[] { record, FlowSets.StageShort(FlowSets.Intake), CancellationToken.None })!;
    }
    
    /// <summary>
    /// Converts payload to dictionary format using Sora.Core.Json for MongoDB-compatible types.
    /// Uses JObject approach to avoid JsonElement and ensure proper Newtonsoft.Json serialization.
    /// </summary>
    private static IDictionary<string, object?>? ToDict(object? payload)
    {
        if (payload is null) return null;
        
        try
        {
            // Use Sora.Core.Json extension which uses JsonDefaults.Settings (Newtonsoft.Json)
            var json = payload.ToJson();
            
            // Parse as JObject then convert to Dictionary to ensure MongoDB-compatible types
            var jObject = JObject.Parse(json);
            var dict = jObject.ToObject<Dictionary<string, object?>>(JsonSerializer.Create(JsonDefaults.Settings));
            
            return dict;
        }
        catch
        {
            // Fallback to simple dictionary wrapping
            return new Dictionary<string, object?> { ["value"] = payload?.ToString() };
        }
    }
}
