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
using System.Collections.Generic;

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
    }

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

    protected virtual async Task WriteToIntake(Type modelType, string model, object payload, string source, dynamic? metadata = null)
    {
        try
        {
            // Create StageRecord with CLEAN separation of payload and metadata
            var stageRecordType = typeof(StageRecord<>).MakeGenericType(modelType);
            var record = Activator.CreateInstance(stageRecordType)!;
            
            // Set basic properties
            stageRecordType.GetProperty("Id")!.SetValue(record, Guid.NewGuid().ToString("n"));
            stageRecordType.GetProperty("SourceId")!.SetValue(record, "flow-orchestrator");
            stageRecordType.GetProperty("OccurredAt")!.SetValue(record, DateTimeOffset.UtcNow);
            
            // CLEAN payload - model data only (no system/adapter contamination)
            stageRecordType.GetProperty("Data")!.SetValue(record, payload);
            
            // SEPARATE metadata - source info for external ID composition
            var stageMetadata = new Dictionary<string, object>
            {
                ["source.system"] = source,
                ["source.adapter"] = source,
                ["transport.type"] = "flow-orchestrator",
                ["transport.timestamp"] = DateTimeOffset.UtcNow
            };
            
            // Add any additional metadata from envelope
            if (metadata != null)
            {
                foreach (var prop in ((JObject)metadata).Properties())
                {
                    stageMetadata[$"envelope.{prop.Name}"] = prop.Value?.ToObject<object>() ?? "";
                }
            }
            
            stageRecordType.GetProperty("StageMetadata")!.SetValue(record, stageMetadata);
            
            // Write to MongoDB intake using Data<,>.UpsertAsync
            var dataType = typeof(Data<,>).MakeGenericType(stageRecordType, typeof(string));
            var upsertMethod = dataType.GetMethod("UpsertAsync");
            if (upsertMethod != null)
            {
                var task = (Task)upsertMethod.Invoke(null, new object[] { record, "flow.intake" })!;
                await task;
                
                Logger.LogDebug("Successfully wrote {Model} to intake with clean metadata separation", model);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error writing {Model} to intake", model);
        }
    }
}