using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Sora.Core.BackgroundServices;
using Sora.Core.Json;
using Sora.Data.Core;
using Sora.Flow.Attributes;
using Sora.Flow.Infrastructure;
using Sora.Flow.Model;
using Sora.Messaging;
using Sora.Data.Abstractions;
using Sora.Flow.Core.Interceptors;
using Sora.Flow.Core.Infrastructure;
using System.Collections.Generic;
using System.Reflection;

namespace Sora.Flow.Core.Orchestration;

/// <summary>
/// Base class for Flow orchestrators that process Flow entity messages from the dedicated queue.
/// Provides type-safe deserialization and clean metadata separation.
/// Now uses Sora Background Services for improved orchestration.
/// </summary>
[FlowOrchestrator]
[SoraBackgroundService(RunInProduction = true)]
[ServiceEvent(Sora.Core.Events.SoraServiceEvents.Flow.EntityProcessed, EventArgsType = typeof(FlowEntityProcessedEventArgs))]
[ServiceEvent(Sora.Core.Events.SoraServiceEvents.Flow.EntityParked, EventArgsType = typeof(FlowEntityParkedEventArgs))]
[ServiceEvent(Sora.Core.Events.SoraServiceEvents.Flow.EntityFailed, EventArgsType = typeof(FlowEntityFailedEventArgs))]
public abstract class FlowOrchestratorBase : SoraFluentServiceBase, IFlowOrchestrator
{
    protected readonly IServiceProvider ServiceProvider;

    protected FlowOrchestratorBase(ILogger logger, IConfiguration configuration, IServiceProvider serviceProvider)
        : base(logger, configuration)
    {
        ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        
        // Call Configure to register Flow.OnUpdate handlers
        Configure();
    }
    
    /// <summary>
    /// Override this method to configure Flow.OnUpdate handlers.
    /// </summary>
    protected virtual void Configure() { }

    public override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        // Auto-subscribe to "Sora.Flow.FlowEntity" queue
        // This is handled by SoraAutoRegistrar during service registration
        Logger.LogInformation("FlowOrchestrator started and listening for Flow entity messages");
        
        // Keep the service running
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    [ServiceAction(Sora.Core.Actions.SoraServiceActions.Flow.ProcessFlowEntity, RequiresParameters = true, ParametersType = typeof(object))]
    public virtual async Task ProcessFlowEntityAction(object transportEnvelope, CancellationToken cancellationToken)
    {
        await ProcessFlowEntity(transportEnvelope);
    }

    [ServiceAction(Sora.Core.Actions.SoraServiceActions.Flow.TriggerProcessing)]
    public virtual async Task TriggerProcessingAction(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Manual processing trigger received");
        // Could trigger additional processing here
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
            
            await EmitEventAsync(Sora.Core.Events.SoraServiceEvents.Flow.EntityProcessed, new FlowEntityProcessedEventArgs
            {
                Type = type,
                Model = model,
                Source = source,
                ProcessedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing Flow entity transport envelope");
            
            await EmitEventAsync(Sora.Core.Events.SoraServiceEvents.Flow.EntityFailed, new FlowEntityFailedEventArgs
            {
                Error = ex.Message,
                FailedAt = DateTimeOffset.UtcNow,
                Exception = ex
            });
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
        
        var payload = envelope.payload;
        
        var modelType = FlowRegistry.ResolveModel(model);
        if (modelType == null)
        {
            Logger.LogWarning("Could not resolve model type for: {Model}", model);
            return;
        }
        
        // Ensure we are working with a JObject
        JObject? payloadJObject = payload as JObject;
        if (payloadJObject == null)
        {
            if (payload is IDictionary<string, object> dictPayload)
            {
                payloadJObject = JObject.FromObject(dictPayload);
            }
            else
            {
                Logger.LogWarning("Unexpected DynamicTransportEnvelope payload type {PayloadType} for model: {Model}", (object)payload.GetType().Name, (object)model);
                return;
            }
        }
        
        var pathValues = payloadJObject;
        
        Logger.LogDebug("DynamicTransportEnvelope path values count: {Count}, keys: {Keys}", 
            pathValues.Count, string.Join(", ", pathValues.Properties().Select(p => p.Name)));
        
        try
        {
            var extensionMethod = typeof(DynamicFlowExtensions)
                .GetMethod("ToDynamicFlowEntity", new[] { typeof(JObject) })!
                .MakeGenericMethod(modelType);
            
            var dynamicEntity = extensionMethod.Invoke(null, new object[] { pathValues });
            
            if (dynamicEntity is IDynamicFlowEntity entity)
            {
                var modelKeys = entity.Model != null ? string.Join(", ", entity.Model.Properties().Select(p => p.Name)) : "null";
                Logger.LogDebug("Created DynamicFlowEntity: {EntityType}, Model keys: {ModelKeys}", 
                    dynamicEntity.GetType().Name, modelKeys);
                
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
            
            // First, check for new fluent BeforeIntake interceptors
            var registry = FlowInterceptorRegistryManager.GetFor(modelType);
            if (registry?.HasBeforeIntakeNonGeneric() == true)
            {
                var actionResult = await registry.ExecuteBeforeIntakeNonGeneric(payload);
                
                if (actionResult is FlowIntakeAction action)
                {
                    Logger.LogDebug("BeforeIntake interceptor processed {Model}: Action={ActionType}, Reason={Reason}", 
                        model, action.Action, action.Reason ?? "none");
                    
                    // Handle fluent interceptor actions
                    switch (action.Action)
                    {
                        case FlowIntakeActionType.Drop:
                            Logger.LogInformation("BeforeIntake interceptor requested DROP for {Model}: {Reason}", model, action.Reason);
                            return;
                        
                        case FlowIntakeActionType.Park:
                            Logger.LogInformation("BeforeIntake interceptor requested PARK for {Model}: {Reason}", model, action.Reason);
                            if (metadata == null) metadata = new JObject();
                            ((JObject)metadata)["parkingStatus"] = action.Reason;
                            shouldPark = true;
                            break;
                            
                        case FlowIntakeActionType.Continue:
                            // Update payload if modified
                            if (action.Entity != payload)
                            {
                                payload = action.Entity;
                            }
                            break;
                    }
                }
            }
            
            // Legacy intake interceptors (for backward compatibility)
            if (FlowIntakeInterceptors.HasInterceptor(modelType))
            {
                var result = FlowIntakeInterceptors.Intercept(payload);
                payload = result.Payload;
                
                Logger.LogDebug("Legacy intake interceptor processed {Model}: MustDrop={MustDrop}, ParkingStatus={ParkingStatus}", 
                    model, result.MustDrop, result.ParkingStatus ?? "none");
                
                // Handle legacy interceptor instructions
                if (result.MustDrop)
                {
                    Logger.LogInformation("Legacy intake interceptor requested DROP for {Model} - skipping processing", model);
                    return;
                }
                
                if (!string.IsNullOrEmpty(result.ParkingStatus))
                {
                    // Legacy interceptor wants to park this record
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
            
            // Extract entity's native ID for SourceId - this preserves lineage for external ID generation
            var entityId = ExtractEntityId(payload);
            stageRecordType.GetProperty("SourceId")!.SetValue(record, entityId ?? source);
            
            stageRecordType.GetProperty("OccurredAt")!.SetValue(record, DateTimeOffset.UtcNow);
            
            // CLEAN payload - model data only (no system/adapter contamination)
            // Handle DynamicFlowEntity objects by preserving the wrapper structure
            object dataToStore = payload;
            if (payload is IDynamicFlowEntity dynamicEntity)
            {
                if (dynamicEntity.Model != null)
                {
                    // For DynamicFlowEntity, we need to preserve the wrapper structure
                    // but ensure the Model property is properly set
                    
                    // Store the full DynamicFlowEntity wrapper to maintain structure for association worker
                    dataToStore = payload;
                }
                else
                {
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
                
                await EmitEventAsync(Sora.Core.Events.SoraServiceEvents.Flow.EntityParked, new FlowEntityParkedEventArgs
                {
                    Model = model,
                    ReasonCode = reasonCode,
                    ParkedAt = DateTimeOffset.UtcNow
                });
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

    /// <summary>
    /// Extracts the entity's native ID for SourceId preservation.
    /// This ensures proper lineage tracking and external ID generation.
    /// </summary>
    private static string? ExtractEntityId(object payload)
    {
        try
        {
            // Try to get the Id property from the entity
            var payloadType = payload.GetType();
            var idProperty = payloadType.GetProperty("Id");
            if (idProperty != null)
            {
                var idValue = idProperty.GetValue(payload);
                return idValue?.ToString();
            }
            
            // For DynamicFlowEntity, try to get the Id from the wrapper itself
            if (payload is IDynamicFlowEntity dynamicEntity)
            {
                var dynamicType = dynamicEntity.GetType();
                var dynamicIdProperty = dynamicType.GetProperty("Id");
                if (dynamicIdProperty != null)
                {
                    var idValue = dynamicIdProperty.GetValue(dynamicEntity);
                    return idValue?.ToString();
                }
            }
            
            return null;
        }
        catch
        {
            // If ID extraction fails, return null and fall back to source
            return null;
        }
    }
}

/// <summary>
/// Event args for when a Flow entity is successfully processed
/// </summary>
public record FlowEntityProcessedEventArgs
{
    public string Type { get; init; } = "";
    public string Model { get; init; } = "";
    public string Source { get; init; } = "";
    public DateTimeOffset ProcessedAt { get; init; }
}

/// <summary>
/// Event args for when a Flow entity is parked for later processing
/// </summary>
public record FlowEntityParkedEventArgs
{
    public string Model { get; init; } = "";
    public string ReasonCode { get; init; } = "";
    public DateTimeOffset ParkedAt { get; init; }
}

/// <summary>
/// Event args for when Flow entity processing fails
/// </summary>
public record FlowEntityFailedEventArgs
{
    public string Error { get; init; } = "";
    public DateTimeOffset FailedAt { get; init; }
    public Exception? Exception { get; init; }
}