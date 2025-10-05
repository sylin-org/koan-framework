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
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Core.Json;
using Koan.Canon.Actions;
using Koan.Canon.Attributes;
using Koan.Canon.Context;
using Koan.Canon.Model;
using Koan.Canon.Infrastructure;
using Koan.Data.Core;
using Koan.Messaging;

namespace Koan.Canon.Initialization;

/// <summary>
/// Initializes Canon messaging by automatically registering transport envelope handlers
/// for all discovered Canon entity types across all loaded assemblies.
/// </summary>
public static class CanonMessagingInitializer
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
            else if (property.Value.Type != JTokenType.Null)
            {
                result[currentPath] = property.Value.ToObject<object>();
            }
        }

        return result;
    }

    /// <summary>
    /// Attempts to get adapter context from call stack when CanonContext.Current is not set.
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
            // Stack trace analysis failed - return null
        }

        return null;
    }


    /// <summary>
    /// Processes a Canon transport envelope into the Canon pipeline stages.
    /// Handles both regular TransportEnvelope and DynamicTransportEnvelope types.
    /// NOTE: This method is preserved for potential future use but is no longer called
    /// since all Canon processing now routes through CanonOrchestrator.
    /// </summary>
    private static async Task ProcessCanonTransportEnvelope(string json)
    {
        try
        {
            // Deserialize the envelope as a dynamic object to extract metadata and payload
            dynamic envelope = Newtonsoft.Json.JsonConvert.DeserializeObject(json)!;

            string model = envelope.model;
            string source = envelope.source;
            object payload = envelope.payload;
            string? correlationId = envelope.metadata?.correlation_id;
            string envelopeType = envelope.type ?? "";

            Console.Error.WriteLine($"[CanonMessagingInitializer] DEBUG: Processing {model} from {source}, envelope type: {envelopeType}");

            // Special handling for DynamicCanonEntity types
            if (envelopeType.Contains("DynamicTransportEnvelope<"))
            {
                Console.Error.WriteLine($"[CanonMessagingInitializer] DEBUG: Converting DynamicTransportEnvelope payload to DynamicCanonEntity");
                payload = ConvertDynamicPayloadToEntity(model, payload);
            }

            // Get the ICanonActions service from the current service provider
            var serviceProvider = AppHost.Current;
            if (serviceProvider == null)
            {
                Console.Error.WriteLine($"[CanonMessagingInitializer] ERROR: AppHost.Current is null, cannot process Canon envelope");
                return;
            }

            var CanonActions = serviceProvider.GetService<ICanonActions>();
            if (CanonActions == null)
            {
                Console.Error.WriteLine($"[CanonMessagingInitializer] ERROR: ICanonActions service not registered");
                return;
            }

            // Generate a reference ID based on the payload
            string referenceId = GenerateReferenceId(payload);

            Console.Error.WriteLine($"[CanonMessagingInitializer] DEBUG: Seeding {model} with referenceId {referenceId} - bypassing CanonActions");

            // PROCESS WITH ORCHESTRATOR - Check for Canon.OnUpdate handlers first
            // If orchestrator exists, process entity before seeding to intake
            try
            {
                var modelType = Koan.Canon.Infrastructure.CanonRegistry.ResolveModel(model);
                if (modelType != null)
                {
                    Console.Error.WriteLine($"[CanonMessagingInitializer] DEBUG: Resolved model type {modelType.Name} for {model}");
                    await ProcessWithOrchestrator(modelType, model, referenceId, payload, source, correlationId);
                    Console.Error.WriteLine($"[CanonMessagingInitializer] DEBUG: Successfully processed {model} through orchestrator pipeline");
                }
                else
                {
                    Console.Error.WriteLine($"[CanonMessagingInitializer] ERROR: Could not resolve model type for {model}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CanonMessagingInitializer] ERROR: Failed to seed {model} directly: {ex.Message}");
                // Fallback to original CanonActions approach
                await CanonActions.SeedAsync(
                    model: model,
                    referenceId: referenceId,
                    payload: payload,
                    correlationId: correlationId
                );
            }

            Console.Error.WriteLine($"[CanonMessagingInitializer] DEBUG: Successfully seeded {model} into Canon pipeline");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CanonMessagingInitializer] ERROR: Failed to process Canon transport envelope: {ex.Message}");
            Console.Error.WriteLine($"[CanonMessagingInitializer] ERROR: Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Converts a DynamicTransportEnvelope payload (dictionary with JSON paths) 
    /// to a proper DynamicCanonEntity with JObject Model property.
    /// </summary>
    private static object ConvertDynamicPayloadToEntity(string model, object payload)
    {
        try
        {
            Console.Error.WriteLine($"[CanonMessagingInitializer] DEBUG: ConvertDynamicPayloadToEntity - model: {model}, payload type: {payload.GetType().Name}");

            // Get the model type for the DynamicCanonEntity
            var modelType = Koan.Canon.Infrastructure.CanonRegistry.ResolveModel(model);
            if (modelType == null)
            {
                Console.Error.WriteLine($"[CanonMessagingInitializer] ERROR: Could not resolve model type for {model}");
                return payload;
            }

            Console.Error.WriteLine($"[CanonMessagingInitializer] DEBUG: Resolved model type: {modelType.Name}");

            // Convert JObject payload to Dictionary<string, object?>
            Dictionary<string, object?> pathValues;
            if (payload is JObject jPayload)
            {
                pathValues = jPayload.ToObject<Dictionary<string, object?>>() ?? new Dictionary<string, object?>();
            }
            else if (payload is Dictionary<string, object?> dictPayload)
            {
                pathValues = dictPayload;
            }
            else
            {
                Console.Error.WriteLine($"[CanonMessagingInitializer] ERROR: Unexpected payload type {payload.GetType().Name}");
                return payload;
            }

            Console.Error.WriteLine($"[CanonMessagingInitializer] DEBUG: Path values count: {pathValues.Count}");
            Console.Error.WriteLine($"[CanonMessagingInitializer] DEBUG: Path values keys: {string.Join(", ", pathValues.Keys)}");

            // Use the ToDynamicCanonEntity extension method to create proper DynamicCanonEntity
            var extensionMethod = typeof(DynamicCanonExtensions)
                .GetMethod("ToDynamicCanonEntity", new[] { typeof(Dictionary<string, object?>) })!
                .MakeGenericMethod(modelType);

            var dynamicEntity = extensionMethod.Invoke(null, new object[] { pathValues });

            Console.Error.WriteLine($"[CanonMessagingInitializer] DEBUG: Created DynamicCanonEntity: {dynamicEntity?.GetType().Name}");

            // Verify Model property is set
            if (dynamicEntity is IDynamicCanonEntity entity)
            {
                var modelKeys = entity.Model != null ? string.Join(", ", entity.Model.Properties().Select(p => p.Name)) : "null";
                Console.Error.WriteLine($"[CanonMessagingInitializer] DEBUG: DynamicCanonEntity.Model keys: {modelKeys}");
            }

            return dynamicEntity ?? payload;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CanonMessagingInitializer] ERROR: Failed to convert dynamic payload: {ex.Message}");
            Console.Error.WriteLine($"[CanonMessagingInitializer] ERROR: Stack trace: {ex.StackTrace}");
            return payload;
        }
    }

    /// <summary>
    /// Generates a reference ID for the Canon entity based on its payload.
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

                // For DynamicCanonEntity, try to get identifier fields from nested model
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
    /// Discovers all Canon entity types across all loaded assemblies.
    /// Returns CanonEntity&lt;T&gt;, DynamicCanonEntity&lt;T&gt;, and CanonValueObject&lt;T&gt; types.
    /// </summary>
    public static List<Type> DiscoverAllCanonTypes()
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
    /// Based on the pattern from KoanAutoRegistrar.
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
        catch (Exception)
        {
            // Swallow errors - some assemblies might not be accessible
        }

        return result;
    }

    /// <summary>
    /// Processes entity through orchestrator pipeline if Canon.OnUpdate handler exists,
    /// then seeds to intake.
    /// </summary>
    private static async Task ProcessWithOrchestrator(Type modelType, string model, string referenceId, object payload, string source, string? correlationId)
    {
        try
        {
            // Check if there's a Canon.OnUpdate handler for this model type
            Console.Error.WriteLine($"[CanonMessagingInitializer] DEBUG: Checking for Canon.OnUpdate handler for {modelType.Name}");
            var hasHandler = Koan.Canon.Core.Orchestration.Canon.HasHandler(modelType);
            Console.Error.WriteLine($"[CanonMessagingInitializer] DEBUG: HasHandler result: {hasHandler}");
            if (hasHandler)
            {
                Console.Error.WriteLine($"[CanonMessagingInitializer] DEBUG: Found Canon.OnUpdate handler for {modelType.Name}");

                // Deserialize payload to strongly-typed object
                var typedPayload = ((JObject)payload).ToObject(modelType);
                if (typedPayload is Koan.Data.Abstractions.IEntity<string> entity)
                {
                    Console.Error.WriteLine($"[CanonMessagingInitializer] DEBUG: Calling orchestrator for {modelType.Name}");

                    // Call the orchestrator handler
                    var handler = Koan.Canon.Core.Orchestration.Canon.GetHandler(modelType);
                    if (handler != null)
                    {
                        // Create metadata for handler
                        var updateMetadata = new Koan.Canon.Core.Orchestration.UpdateMetadata
                        {
                            SourceSystem = source ?? "unknown",
                            SourceAdapter = source ?? "unknown",
                            Timestamp = DateTimeOffset.UtcNow
                        };

                        // Use reflection to invoke the handler with ref parameter
                        var method = typeof(CanonMessagingInitializer)
                            .GetMethod(nameof(InvokeOrchestrator), BindingFlags.NonPublic | BindingFlags.Static)!
                            .MakeGenericMethod(modelType);

                        var result = await (Task<Koan.Canon.Core.Orchestration.UpdateResult>)method.Invoke(null, new object[] { handler, entity, null, updateMetadata })!;

                        if (result.Action == Koan.Canon.Core.Orchestration.UpdateAction.Skip)
                        {
                            Console.Error.WriteLine($"[CanonMessagingInitializer] INFO: Orchestrator skipped {modelType.Name}: {result.Reason}");
                            return; // Don't seed to intake
                        }

                        Console.Error.WriteLine($"[CanonMessagingInitializer] DEBUG: Orchestrator processed {modelType.Name}: {result.Reason}");

                        // Update payload with potentially modified entity
                        payload = entity;
                    }
                }
            }
            else
            {
                Console.Error.WriteLine($"[CanonMessagingInitializer] DEBUG: No Canon.OnUpdate handler found for {modelType.Name}");
            }

            // Proceed with seeding to intake (with potentially modified payload)
            await DirectSeedToIntake(modelType, model, referenceId, payload, source, correlationId);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CanonMessagingInitializer] ERROR: Failed to process {modelType.Name} with orchestrator: {ex.Message}");
            Console.Error.WriteLine($"[CanonMessagingInitializer] ERROR: Stack trace: {ex.StackTrace}");

            // Fallback to direct seeding
            await DirectSeedToIntake(modelType, model, referenceId, payload, source, correlationId);
        }
    }

    /// <summary>
    /// Invokes a Canon.OnUpdate handler with proper generic typing and ref parameter semantics.
    /// </summary>
    private static async Task<Koan.Canon.Core.Orchestration.UpdateResult> InvokeOrchestrator<T>(object handler, T proposed, T? current, Koan.Canon.Core.Orchestration.UpdateMetadata metadata) where T : class, Koan.Data.Abstractions.IEntity<string>
    {
        var typedHandler = (Koan.Canon.Core.Orchestration.UpdateHandler<T>)handler;
        return await typedHandler(ref proposed, current, metadata);
    }

    /// <summary>
    /// Directly seeds payload into Canon intake stage, bypassing CanonAction messaging.
    /// Replicates CanonActionHandler.HandleSeedAsync logic.
    /// </summary>
    private static async Task DirectSeedToIntake(Type modelType, string model, string referenceId, object payload, string source, string? correlationId)
    {
        // Create StageRecord<TModel> and save to intake set (from CanonActionHandler.HandleSeedAsync)
        var recordType = typeof(StageRecord<>).MakeGenericType(modelType);
        var record = Activator.CreateInstance(recordType)!;
        recordType.GetProperty("Id")!.SetValue(record, Guid.CreateVersion7().ToString("n"));
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

        // Save to MongoDB using Data<,>.UpsertAsync (from CanonActionHandler.HandleSeedAsync)
        var dataType = typeof(Data<,>).MakeGenericType(recordType, typeof(string));
        var upsert = dataType.GetMethod("UpsertAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, new[] { recordType, typeof(string), typeof(CancellationToken) })!;
        await (Task)upsert.Invoke(null, new object?[] { record, CanonSets.StageShort(CanonSets.Intake), CancellationToken.None })!;
    }

    /// <summary>
    /// Converts payload to dictionary format using Koan.Core.Json for MongoDB-compatible types.
    /// Uses JObject approach to avoid JsonElement and ensure proper Newtonsoft.Json serialization.
    /// Special handling for DynamicCanonEntity to preserve Model structure.
    /// </summary>
    private static object? ToDict(object? payload)
    {
        if (payload is null) return null;

        try
        {
            // Special handling for DynamicCanonEntity - return the full entity
            if (payload is IDynamicCanonEntity dynamicEntity)
            {
                Console.Error.WriteLine($"[CanonMessagingInitializer] DEBUG: ToDict - preserving DynamicCanonEntity structure");
                Console.Error.WriteLine($"[CanonMessagingInitializer] DEBUG: ToDict - Model keys: {(dynamicEntity.Model != null ? string.Join(", ", dynamicEntity.Model.Properties().Select(p => p.Name)) : "null")}");
                return payload; // Return the full DynamicCanonEntity, not just a dictionary
            }

            // Use Koan.Core.Json extension which uses JsonDefaults.Settings (Newtonsoft.Json)
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



