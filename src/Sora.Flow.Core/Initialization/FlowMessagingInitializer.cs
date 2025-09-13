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
    /// Processes a Flow transport envelope into the Flow pipeline stages.
    /// Handles both regular TransportEnvelope and DynamicTransportEnvelope types.
    /// NOTE: This method is preserved for potential future use but is no longer called
    /// since all Flow processing now routes through FlowOrchestrator.
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
            string envelopeType = envelope.type ?? "";

            Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Processing {model} from {source}, envelope type: {envelopeType}");

            // Special handling for DynamicFlowEntity types
            if (envelopeType.Contains("DynamicTransportEnvelope<"))
            {
                Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Converting DynamicTransportEnvelope payload to DynamicFlowEntity");
                payload = ConvertDynamicPayloadToEntity(model, payload);
            }

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

            // PROCESS WITH ORCHESTRATOR - Check for Flow.OnUpdate handlers first
            // If orchestrator exists, process entity before seeding to intake
            try
            {
                var modelType = Sora.Flow.Infrastructure.FlowRegistry.ResolveModel(model);
                if (modelType != null)
                {
                    Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Resolved model type {modelType.Name} for {model}");
                    await ProcessWithOrchestrator(modelType, model, referenceId, payload, source, correlationId);
                    Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Successfully processed {model} through orchestrator pipeline");
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
    /// Converts a DynamicTransportEnvelope payload (dictionary with JSON paths) 
    /// to a proper DynamicFlowEntity with JObject Model property.
    /// </summary>
    private static object ConvertDynamicPayloadToEntity(string model, object payload)
    {
        try
        {
            Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: ConvertDynamicPayloadToEntity - model: {model}, payload type: {payload.GetType().Name}");

            // Get the model type for the DynamicFlowEntity
            var modelType = Sora.Flow.Infrastructure.FlowRegistry.ResolveModel(model);
            if (modelType == null)
            {
                Console.Error.WriteLine($"[FlowMessagingInitializer] ERROR: Could not resolve model type for {model}");
                return payload;
            }

            Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Resolved model type: {modelType.Name}");

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
                Console.Error.WriteLine($"[FlowMessagingInitializer] ERROR: Unexpected payload type {payload.GetType().Name}");
                return payload;
            }

            Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Path values count: {pathValues.Count}");
            Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Path values keys: {string.Join(", ", pathValues.Keys)}");

            // Use the ToDynamicFlowEntity extension method to create proper DynamicFlowEntity
            var extensionMethod = typeof(DynamicFlowExtensions)
                .GetMethod("ToDynamicFlowEntity", new[] { typeof(Dictionary<string, object?>) })!
                .MakeGenericMethod(modelType);

            var dynamicEntity = extensionMethod.Invoke(null, new object[] { pathValues });

            Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Created DynamicFlowEntity: {dynamicEntity?.GetType().Name}");

            // Verify Model property is set
            if (dynamicEntity is IDynamicFlowEntity entity)
            {
                var modelKeys = entity.Model != null ? string.Join(", ", entity.Model.Properties().Select(p => p.Name)) : "null";
                Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: DynamicFlowEntity.Model keys: {modelKeys}");
            }

            return dynamicEntity ?? payload;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FlowMessagingInitializer] ERROR: Failed to convert dynamic payload: {ex.Message}");
            Console.Error.WriteLine($"[FlowMessagingInitializer] ERROR: Stack trace: {ex.StackTrace}");
            return payload;
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
    /// Returns FlowEntity&lt;T&gt;, DynamicFlowEntity&lt;T&gt;, and FlowValueObject&lt;T&gt; types.
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
        }

        return result;
    }

    /// <summary>
    /// Processes entity through orchestrator pipeline if Flow.OnUpdate handler exists,
    /// then seeds to intake.
    /// </summary>
    private static async Task ProcessWithOrchestrator(Type modelType, string model, string referenceId, object payload, string source, string? correlationId)
    {
        try
        {
            // Check if there's a Flow.OnUpdate handler for this model type
            Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Checking for Flow.OnUpdate handler for {modelType.Name}");
            var hasHandler = Sora.Flow.Core.Orchestration.Flow.HasHandler(modelType);
            Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: HasHandler result: {hasHandler}");
            if (hasHandler)
            {
                Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Found Flow.OnUpdate handler for {modelType.Name}");

                // Deserialize payload to strongly-typed object
                var typedPayload = ((JObject)payload).ToObject(modelType);
                if (typedPayload is Sora.Data.Abstractions.IEntity<string> entity)
                {
                    Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Calling orchestrator for {modelType.Name}");

                    // Call the orchestrator handler
                    var handler = Sora.Flow.Core.Orchestration.Flow.GetHandler(modelType);
                    if (handler != null)
                    {
                        // Create metadata for handler
                        var updateMetadata = new Sora.Flow.Core.Orchestration.UpdateMetadata
                        {
                            SourceSystem = source ?? "unknown",
                            SourceAdapter = source ?? "unknown",
                            Timestamp = DateTimeOffset.UtcNow
                        };

                        // Use reflection to invoke the handler with ref parameter
                        var method = typeof(FlowMessagingInitializer)
                            .GetMethod(nameof(InvokeOrchestrator), BindingFlags.NonPublic | BindingFlags.Static)!
                            .MakeGenericMethod(modelType);

                        var result = await (Task<Sora.Flow.Core.Orchestration.UpdateResult>)method.Invoke(null, new object[] { handler, entity, null, updateMetadata })!;

                        if (result.Action == Sora.Flow.Core.Orchestration.UpdateAction.Skip)
                        {
                            Console.Error.WriteLine($"[FlowMessagingInitializer] INFO: Orchestrator skipped {modelType.Name}: {result.Reason}");
                            return; // Don't seed to intake
                        }

                        Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: Orchestrator processed {modelType.Name}: {result.Reason}");

                        // Update payload with potentially modified entity
                        payload = entity;
                    }
                }
            }
            else
            {
                Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: No Flow.OnUpdate handler found for {modelType.Name}");
            }

            // Proceed with seeding to intake (with potentially modified payload)
            await DirectSeedToIntake(modelType, model, referenceId, payload, source, correlationId);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FlowMessagingInitializer] ERROR: Failed to process {modelType.Name} with orchestrator: {ex.Message}");
            Console.Error.WriteLine($"[FlowMessagingInitializer] ERROR: Stack trace: {ex.StackTrace}");

            // Fallback to direct seeding
            await DirectSeedToIntake(modelType, model, referenceId, payload, source, correlationId);
        }
    }

    /// <summary>
    /// Invokes a Flow.OnUpdate handler with proper generic typing and ref parameter semantics.
    /// </summary>
    private static async Task<Sora.Flow.Core.Orchestration.UpdateResult> InvokeOrchestrator<T>(object handler, T proposed, T? current, Sora.Flow.Core.Orchestration.UpdateMetadata metadata) where T : class, Sora.Data.Abstractions.IEntity<string>
    {
        var typedHandler = (Sora.Flow.Core.Orchestration.UpdateHandler<T>)handler;
        return await typedHandler(ref proposed, current, metadata);
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
    /// Special handling for DynamicFlowEntity to preserve Model structure.
    /// </summary>
    private static object? ToDict(object? payload)
    {
        if (payload is null) return null;

        try
        {
            // Special handling for DynamicFlowEntity - return the full entity
            if (payload is IDynamicFlowEntity dynamicEntity)
            {
                Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: ToDict - preserving DynamicFlowEntity structure");
                Console.Error.WriteLine($"[FlowMessagingInitializer] DEBUG: ToDict - Model keys: {(dynamicEntity.Model != null ? string.Join(", ", dynamicEntity.Model.Properties().Select(p => p.Name)) : "null")}");
                return payload; // Return the full DynamicFlowEntity, not just a dictionary
            }

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
