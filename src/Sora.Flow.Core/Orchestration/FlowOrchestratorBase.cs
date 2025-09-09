using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Sora.Core.Json;
using Sora.Data.Core;
using Sora.Flow.Attributes;
using Sora.Flow.Infrastructure;
using Sora.Flow.Model;
using Sora.Messaging;
using Sora.Data.Abstractions;
using Sora.Flow.Core.Interceptors;
using System.Collections.Generic;
using System.Reflection;

namespace Sora.Flow.Core.Orchestration;

/// <summary>
/// Base class for Flow orchestrators that process Flow entity messages from the dedicated queue.
/// Provides type-safe deserialization and clean metadata separation.
/// </summary>
[FlowOrchestrator]
public abstract class FlowOrchestratorBase : BackgroundService, IFlowOrchestrator
{
    protected readonly ILogger Logger;
    protected readonly IServiceProvider ServiceProvider;

    protected FlowOrchestratorBase(ILogger logger, IServiceProvider serviceProvider)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        
        // Call Configure to register Flow.OnUpdate handlers
        Configure();
    }
    
    /// <summary>
    /// Override this method to configure Flow.OnUpdate handlers.
    /// </summary>
    protected virtual void Configure() { }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Auto-subscribe to "Sora.Flow.FlowEntity" queue
        // This is handled by SoraAutoRegistrar during service registration
        Logger.LogInformation("FlowOrchestrator started and listening for Flow entity messages");
        
        // Keep the service running
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    public virtual async Task ProcessFlowEntity(object transportEnvelope)
    {
        try
        {
            // Deserialize the transport envelope
            dynamic envelope = JObject.Parse(transportEnvelope.ToString()!);
            
            string type = envelope.type;
            string model = envelope.model;
            string source = envelope.source ?? "unknown";
            
            Logger.LogDebug("Processing Flow entity: Type={Type}, Model={Model}, Source={Source}", type, model, source);
            
            // Type-safe processing based on envelope type
            if (type.StartsWith("FlowEntity<") || type.StartsWith("FlowValueObject<"))
            {
                await ProcessFlowEntity(envelope, model, source);
            }
            else if (type.StartsWith("DynamicFlowEntity<"))
            {
                await ProcessDynamicFlowEntity(envelope, model, source);
            }
            else if (type.StartsWith("TransportEnvelope<"))
            {
                await ProcessTransportEnvelope(envelope, model, source);
            }
            else if (type.StartsWith("DynamicTransportEnvelope<"))
            {
                await ProcessDynamicTransportEnvelope(envelope, model, source);
            }
            else
            {
                Logger.LogWarning("Unknown Flow entity type: {Type}", type);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing Flow entity transport envelope");
        }
    }

    protected virtual async Task ProcessFlowEntity(dynamic envelope, string model, string source)
    {
        // Extract payload
        var payload = envelope.payload;
        
        // Resolve model type
        var modelType = FlowRegistry.ResolveModel(model);
        if (modelType == null)
        {
            Logger.LogWarning("Could not resolve model type for: {Model}", model);
            return;
        }
        
        // Deserialize payload to strongly-typed object
        var typedPayload = ((JObject)payload).ToObject(modelType);
        if (typedPayload == null)
        {
            Logger.LogWarning("Failed to deserialize payload for model: {Model}", model);
            return;
        }
        
        // Write to intake with clean metadata separation
        await WriteToIntake(modelType, model, typedPayload, source, envelope.metadata);
    }

    protected virtual async Task ProcessDynamicFlowEntity(dynamic envelope, string model, string source)
    {
        // Extract flattened payload for dynamic entities
        var payload = envelope.payload;
        
        // Resolve model type
        var modelType = FlowRegistry.ResolveModel(model);
        if (modelType == null)
        {
            Logger.LogWarning("Could not resolve model type for: {Model}", model);
            return;
        }
        
        // For dynamic entities, the payload is already flattened
        var flatPayload = ((JObject)payload).ToObject<Dictionary<string, object?>>();
        if (flatPayload == null)
        {
            Logger.LogWarning("Failed to deserialize dynamic payload for model: {Model}", model);
            return;
        }
        
        // Write to intake with clean metadata separation
        await WriteToIntake(modelType, model, flatPayload, source, envelope.metadata);
    }

    protected virtual async Task ProcessTransportEnvelope(dynamic envelope, string model, string source)
    {
        // Extract payload from transport envelope
        var payload = envelope.payload;
        
        // Resolve model type
        var modelType = FlowRegistry.ResolveModel(model);
        if (modelType == null)
        {
            Logger.LogWarning("Could not resolve model type for: {Model}", model);
            return;
        }
        
        // For TransportEnvelope, deserialize payload to strongly-typed object
        var typedPayload = ((JObject)payload).ToObject(modelType);
        if (typedPayload == null)
        {
            Logger.LogWarning("Failed to deserialize transport envelope payload for model: {Model}", model);
            return;
        }
        
        Logger.LogDebug("Processing TransportEnvelope for {Model} from {Source}", model, source);
        
        // Write to intake with clean metadata separation
        await WriteToIntake(modelType, model, typedPayload, source, envelope.metadata);
    }

    protected virtual async Task ProcessDynamicTransportEnvelope(dynamic envelope, string model, string source)
    {
        Logger.LogDebug("Processing DynamicTransportEnvelope for {Model} from {Source}", model, source);
        
        // Extract flattened payload from dynamic transport envelope
        var payload = envelope.payload;
        
        // Resolve model type
        var modelType = FlowRegistry.ResolveModel(model);
        if (modelType == null)
        {
            Logger.LogWarning("Could not resolve model type for: {Model}", model);
            return;
        }
        
        // Convert JObject payload to Dictionary<string, object?>
        Dictionary<string, object?> pathValues;
        if (payload is JObject jPayload)
        {
            pathValues = jPayload.ToObject<Dictionary<string, object?>>() ?? new Dictionary<string, object?>();
        }
        else if (payload is IDictionary<string, object?> dictPayload)
        {
            pathValues = new Dictionary<string, object?>(dictPayload);
        }
        else
        {
            Logger.LogWarning("Unexpected DynamicTransportEnvelope payload type {PayloadType} for model: {Model}", (object)payload.GetType().Name, (object)model);
            return;
        }
        
        Logger.LogDebug("DynamicTransportEnvelope path values count: {Count}, keys: {Keys}", 
            pathValues.Count, string.Join(", ", pathValues.Keys));
        
        // Use the ToDynamicFlowEntity extension method to create proper DynamicFlowEntity
        try
        {
            var extensionMethod = typeof(DynamicFlowExtensions)
                .GetMethod("ToDynamicFlowEntity", new[] { typeof(Dictionary<string, object?>) })!
                .MakeGenericMethod(modelType);
            
            var dynamicEntity = extensionMethod.Invoke(null, new object[] { pathValues });
            
            if (dynamicEntity is IDynamicFlowEntity entity)
            {
                var modelKeys = entity.Model != null ? string.Join(", ", ((IDictionary<string, object?>)entity.Model).Keys) : "null";
                Logger.LogDebug("Created DynamicFlowEntity: {EntityType}, Model keys: {ModelKeys}", 
                    dynamicEntity.GetType().Name, modelKeys);
                
                // Write the reconstructed DynamicFlowEntity to intake
                await WriteToIntake(modelType, model, dynamicEntity, source, envelope.metadata);
            }
            else
            {
                Logger.LogWarning("Failed to create DynamicFlowEntity for model: {Model}", model);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating DynamicFlowEntity for model: {Model}", model);
        }
    }

    protected virtual async Task WriteToIntake(Type modelType, string model, object payload, string source, dynamic? metadata = null)
    {
        try
        {
            bool shouldPark = false;
            
            // First, check for intake interceptors
            if (FlowIntakeInterceptors.HasInterceptor(modelType))
            {
                var result = FlowIntakeInterceptors.Intercept(payload);
                payload = result.Payload;
                
                Logger.LogDebug("Intake interceptor processed {Model}: MustDrop={MustDrop}, ParkingStatus={ParkingStatus}", 
                    model, result.MustDrop, result.ParkingStatus ?? "none");
                
                // Handle interceptor instructions
                if (result.MustDrop)
                {
                    Logger.LogInformation("Intake interceptor requested DROP for {Model} - skipping processing", model);
                    return;
                }
                
                if (!string.IsNullOrEmpty(result.ParkingStatus))
                {
                    // Interceptor wants to park this record - handle via default intake processing
                    if (metadata == null)
                    {
                        metadata = new JObject();
                    }
                    ((JObject)metadata)["parking.status"] = result.ParkingStatus;
                    ((JObject)metadata)["parking.reason"] = "interceptor";
                    shouldPark = true;
                    
                    Logger.LogDebug("Intake interceptor requested PARK for {Model} with status {ParkingStatus}", 
                        model, result.ParkingStatus);
                }
            }
            
            // If parking was requested by interceptor, always use default intake processing
            // This ensures proper parking behavior regardless of Flow.OnUpdate handlers
            if (shouldPark)
            {
                await WriteToIntakeDefault(modelType, model, payload, source, metadata);
                return;
            }
            
            // Check if there's a Flow.OnUpdate handler for this model type
            if (Flow.HasHandler(modelType) && payload is IEntity<string> entity)
            {
                await ProcessWithFlowHandler(modelType, entity, source, metadata);
                return;
            }
            
            // Fall back to default intake processing
            await WriteToIntakeDefault(modelType, model, payload, source, metadata);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing {Model} in WriteToIntake", model);
        }
    }
    
    /// <summary>
    /// Process entity using registered Flow.OnUpdate handlers.
    /// </summary>
    private async Task ProcessWithFlowHandler(Type modelType, IEntity<string> proposed, string source, dynamic? metadata = null)
    {
        try
        {
            Logger.LogDebug("Processing {Model} with Flow.OnUpdate handler", modelType.Name);
            
            // Get current canonical entity from database (if exists)
            IEntity<string>? current = null;
            if (!string.IsNullOrEmpty(proposed.Id))
            {
                current = await GetCurrentCanonical(modelType, proposed.Id);
            }
            
            // Create metadata for handler
            var updateMetadata = new UpdateMetadata
            {
                SourceSystem = source,
                SourceAdapter = source,
                Timestamp = DateTimeOffset.UtcNow
            };
            
            if (metadata != null)
            {
                foreach (var prop in ((JObject)metadata).Properties())
                {
                    updateMetadata.Properties[prop.Name] = prop.Value?.ToObject<object>() ?? "";
                }
            }
            
            // Get handler using reflection since we have Type, not T
            var handler = Flow.GetHandler(modelType);
            if (handler != null)
            {
                // Call handler with ref parameter - use reflection to invoke generic method
                var method = typeof(FlowOrchestratorBase)
                    .GetMethod(nameof(InvokeFlowHandler), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(modelType);
                
                var result = await (Task<UpdateResult>)method.Invoke(this, new object[] { handler, proposed, current, updateMetadata })!;
                
                if (result.Action == UpdateAction.Skip)
                {
                    Logger.LogInformation("Skipped {Model}: {Reason}", modelType.Name, result.Reason);
                    return;
                }
                
                if (result.Action == UpdateAction.Defer)
                {
                    Logger.LogInformation("Deferred {Model}: {Reason}", modelType.Name, result.Reason);
                    // TODO: Implement retry logic
                    return;
                }
                
                Logger.LogDebug("Flow handler processed {Model}: {Reason}", modelType.Name, result.Reason);
            }
            
            // Continue with normal flow processing - save the (possibly modified) entity
            await SaveEntityToCanonical(proposed);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing {Model} with Flow handler", modelType.Name);
            throw;
        }
    }
    
    /// <summary>
    /// Invoke Flow.OnUpdate handler with proper generic typing.
    /// </summary>
    private async Task<UpdateResult> InvokeFlowHandler<T>(object handler, T proposed, T? current, UpdateMetadata metadata) where T : class, IEntity<string>
    {
        var typedHandler = (UpdateHandler<T>)handler;
        return await typedHandler(ref proposed, current, metadata);
    }
    
    /// <summary>
    /// Get current canonical entity from database.
    /// </summary>
    private async Task<IEntity<string>?> GetCurrentCanonical(Type modelType, string id)
    {
        try
        {
            // Use Data<T, TKey>.GetAsync to retrieve current canonical
            var dataType = typeof(Data<,>).MakeGenericType(modelType, typeof(string));
            var getMethod = dataType.GetMethod("GetAsync", new[] { typeof(string) });
            if (getMethod != null)
            {
                var task = (Task)getMethod.Invoke(null, new object[] { id })!;
                await task;
                
                var result = task.GetType().GetProperty("Result")?.GetValue(task);
                return result as IEntity<string>;
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug("Could not retrieve current canonical for {ModelType} {Id}: {Error}", modelType.Name, id, ex.Message);
        }
        
        return null;
    }
    
    /// <summary>
    /// Save entity to canonical collection.
    /// </summary>
    private async Task SaveEntityToCanonical(IEntity<string> entity)
    {
        try
        {
            // Use the entity's Save() method to persist to canonical
            var saveMethod = entity.GetType().GetMethod("Save", new Type[] { });
            if (saveMethod != null)
            {
                var task = (Task)saveMethod.Invoke(entity, null)!;
                await task;
                Logger.LogDebug("Saved {ModelType} {Id} to canonical", entity.GetType().Name, entity.Id);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error saving {ModelType} {Id} to canonical", entity.GetType().Name, entity.Id);
            throw;
        }
    }
    
    /// <summary>
    /// Default intake processing when no Flow.OnUpdate handler is registered.
    /// </summary>
    protected virtual async Task WriteToIntakeDefault(Type modelType, string model, object payload, string source, dynamic? metadata = null)
    {
        try
        {
            // Check if parking is requested
            string? parkingStatus = null;
            if (metadata != null)
            {
                if (((JObject)metadata).TryGetValue("parking.status", out var parkingToken))
                {
                    parkingStatus = parkingToken?.ToString();
                }
            }
            
            if (!string.IsNullOrEmpty(parkingStatus))
            {
                // Create ParkedRecord instead of StageRecord
                await WriteToParked(modelType, model, payload, source, parkingStatus, metadata);
                return;
            }
            
            // Create StageRecord with CLEAN separation of payload and metadata
            var stageRecordType = typeof(StageRecord<>).MakeGenericType(modelType);
            var record = Activator.CreateInstance(stageRecordType)!;
            
            // Set basic properties
            stageRecordType.GetProperty("Id")!.SetValue(record, Guid.NewGuid().ToString("n"));
            stageRecordType.GetProperty("SourceId")!.SetValue(record, "flow-orchestrator");
            stageRecordType.GetProperty("OccurredAt")!.SetValue(record, DateTimeOffset.UtcNow);
            
            // CLEAN payload - model data only (no system/adapter contamination)
            // Handle DynamicFlowEntity objects by preserving the wrapper structure
            object dataToStore = payload;
            Console.WriteLine($"[FlowOrchestrator] Processing payload type: {payload.GetType().Name}");
            if (payload is IDynamicFlowEntity dynamicEntity)
            {
                Console.WriteLine($"[FlowOrchestrator] Found DynamicFlowEntity, Model type: {dynamicEntity.Model?.GetType().Name ?? "null"}");
                if (dynamicEntity.Model != null)
                {
                    // For DynamicFlowEntity, we need to preserve the wrapper structure
                    // but ensure the Model property is properly set
                    Console.WriteLine($"[FlowOrchestrator] Keeping DynamicFlowEntity wrapper with Model data");
                    Console.WriteLine($"[FlowOrchestrator] Model content keys: {string.Join(", ", ((IDictionary<string, object?>)dynamicEntity.Model).Keys)}");
                    
                    // Store the full DynamicFlowEntity wrapper to maintain structure for association worker
                    dataToStore = payload;
                }
                else
                {
                    Console.WriteLine($"[FlowOrchestrator] WARNING: DynamicFlowEntity.Model is null!");
                }
            }
            stageRecordType.GetProperty("Data")!.SetValue(record, dataToStore);
            
            // SEPARATE metadata - source info for external ID composition
            var stageMetadata = new Dictionary<string, object>
            {
                ["source.system"] = source,
                ["source.adapter"] = source,
                ["transport.type"] = "flow-orchestrator",
                ["transport.timestamp"] = DateTimeOffset.UtcNow
            };
            
            // Add any additional metadata from envelope
            if (metadata is JObject jObj)
            {
                foreach (var prop in jObj.Properties())
                {
                    if (prop.Value != null && prop.Value.Type != JTokenType.Null)
                    {
                        // Convert JToken to primitive types only to avoid BsonValue serialization issues
                        var value = ConvertJTokenToPrimitive(prop.Value);
                        if (value != null)
                        {
                            stageMetadata[$"envelope.{prop.Name}"] = value;
                        }
                    }
                }
            }
            
            // Create a deeply cleaned dictionary with no null values for MongoDB serialization
            var cleanMetadata = DeepCleanMetadata(stageMetadata);
            
            stageRecordType.GetProperty("Source")!.SetValue(record, cleanMetadata);
            
            // Write to MongoDB intake using Data<,>.UpsertAsync
            var dataType = typeof(Data<,>).MakeGenericType(stageRecordType, typeof(string));
            var upsertMethod = dataType.GetMethod("UpsertAsync", new[] { stageRecordType, typeof(string), typeof(CancellationToken) });
            if (upsertMethod != null)
            {
                Logger.LogDebug("Invoking UpsertAsync for {Model} with record type {RecordType}", model, stageRecordType.Name);
                var taskResult = upsertMethod.Invoke(null, new object[] { record, "flow.intake", CancellationToken.None });
                if (taskResult is Task task)
                {
                    await task;
                    Logger.LogDebug("Successfully wrote {Model} to intake with clean metadata separation", model);
                }
                else
                {
                    Logger.LogError("UpsertAsync returned null or non-Task result for {Model}: {Result}", model, taskResult);
                }
            }
            else
            {
                Logger.LogError("UpsertAsync method not found for type {DataType}", dataType);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error writing {Model} to intake", model);
        }
    }
    
    /// <summary>
    /// Write a record to the parked collection for later resolution.
    /// </summary>
    protected virtual async Task WriteToParked(Type modelType, string model, object payload, string source, string reasonCode, dynamic? metadata = null)
    {
        try
        {
            // Create ParkedRecord<TModel>
            var parkedRecordType = typeof(ParkedRecord<>).MakeGenericType(modelType);
            var record = Activator.CreateInstance(parkedRecordType)!;
            
            // Set basic properties
            parkedRecordType.GetProperty("Id")!.SetValue(record, Guid.NewGuid().ToString("n"));
            parkedRecordType.GetProperty("SourceId")!.SetValue(record, "flow-orchestrator");
            parkedRecordType.GetProperty("OccurredAt")!.SetValue(record, DateTimeOffset.UtcNow);
            parkedRecordType.GetProperty("ReasonCode")!.SetValue(record, reasonCode);
            
            // Set payload directly as strongly-typed data
            parkedRecordType.GetProperty("Data")!.SetValue(record, payload);
            
            // Set source metadata
            var sourceMetadata = new Dictionary<string, object?>
            {
                ["system"] = source,
                ["adapter"] = source,
                ["transport.type"] = "flow-orchestrator",
                ["transport.timestamp"] = DateTimeOffset.UtcNow
            };
            
            // Add envelope metadata
            if (metadata != null)
            {
                foreach (var prop in ((JObject)metadata).Properties())
                {
                    if (prop.Name != "parking.status" && prop.Name != "parking.reason")
                    {
                        var value = prop.Value != null ? ConvertJTokenToPrimitive(prop.Value) : null;
                        if (value != null)
                        {
                            sourceMetadata[$"envelope.{prop.Name}"] = value;
                        }
                    }
                }
            }
            
            // Deep clean the metadata before setting
            var cleanSourceMetadata = DeepCleanMetadata(sourceMetadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value!));
            parkedRecordType.GetProperty("Source")!.SetValue(record, cleanSourceMetadata);
            
            // Write to MongoDB parked collection 
            var dataType = typeof(Data<,>).MakeGenericType(parkedRecordType, typeof(string));
            var upsertMethod = dataType.GetMethod("UpsertAsync", BindingFlags.Public | BindingFlags.Static, new[] { parkedRecordType, typeof(string), typeof(CancellationToken) });
            if (upsertMethod != null)
            {
                var task = (Task)upsertMethod.Invoke(null, new object[] { record, FlowSets.StageShort(FlowSets.Parked), CancellationToken.None })!;
                await task;
                
                Logger.LogInformation("Successfully parked {Model} with ReasonCode={ReasonCode} for background processing", model, reasonCode);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error parking {Model} with ReasonCode={ReasonCode}", model, reasonCode);
            throw;
        }
    }
    
    /// <summary>
    /// Safely converts JToken to primitive types that can be serialized by MongoDB without BsonValue issues.
    /// </summary>
    private static object? ConvertJTokenToPrimitive(JToken token)
    {
        return token.Type switch
        {
            JTokenType.String => token.Value<string>(),
            JTokenType.Integer => token.Value<long>(),
            JTokenType.Float => token.Value<double>(),
            JTokenType.Boolean => token.Value<bool>(),
            JTokenType.Date => token.Value<DateTime>(),
            JTokenType.TimeSpan => token.Value<TimeSpan>(),
            JTokenType.Guid => token.Value<Guid>(),
            JTokenType.Uri => token.Value<Uri>()?.ToString(),
            JTokenType.Array => token.Select(ConvertJTokenToPrimitive).Where(v => v != null).ToArray(),
            JTokenType.Object => ConvertJObjectToDictionary((JObject)token),
            _ => null
        };
    }
    
    /// <summary>
    /// Recursively converts JObject to Dictionary with only primitive MongoDB-safe types.
    /// </summary>
    private static Dictionary<string, object> ConvertJObjectToDictionary(JObject jObj)
    {
        var result = new Dictionary<string, object>();
        foreach (var prop in jObj.Properties())
        {
            var value = ConvertJTokenToPrimitive(prop.Value);
            if (value != null)
            {
                result[prop.Name] = value;
            }
        }
        return result;
    }
    
    /// <summary>
    /// Deeply cleans metadata by removing all null values recursively to prevent MongoDB BSON serialization errors.
    /// </summary>
    private static Dictionary<string, object> DeepCleanMetadata(Dictionary<string, object> metadata)
    {
        var cleaned = new Dictionary<string, object>();
        
        foreach (var kvp in metadata)
        {
            var cleanValue = CleanValue(kvp.Value);
            if (cleanValue != null)
            {
                cleaned[kvp.Key] = cleanValue;
            }
        }
        
        return cleaned;
    }
    
    /// <summary>
    /// Recursively cleans individual values by removing nulls and converting EVERYTHING to MongoDB-safe primitive types.
    /// This prevents ANY BsonValue serialization issues by being extremely conservative.
    /// </summary>
    private static object? CleanValue(object? value)
    {
        if (value == null)
            return null;
            
        return value switch
        {
            // Only allow the most basic primitive types
            string s => s,
            int i => i,
            long l => l,
            double d => d,
            float f => (double)f,  // Convert float to double for consistency
            bool b => b,
            DateTime dt => dt.ToString("O"),  // Convert to ISO string
            DateTimeOffset dto => dto.ToString("O"),  // Convert to ISO string
            Guid g => g.ToString(),  // Convert to string
            
            // Convert EVERYTHING else to string representation to be 100% safe
            _ => value.ToString() ?? ""
        };
    }

}