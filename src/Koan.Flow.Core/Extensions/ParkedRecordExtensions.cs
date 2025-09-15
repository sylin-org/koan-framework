using Koan.Flow.Actions;
using Koan.Flow.Model;
using Koan.Data.Core;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Flow.Core.Extensions;

/// <summary>
/// Extension methods for ParkedRecord to provide semantic healing operations.
/// </summary>
public static class ParkedRecordExtensions
{
    /// <summary>
    /// Heals a parked record by re-injecting the resolved model data into the Flow pipeline
    /// and removing the parked record. This is the proper Koan.Flow pattern for 
    /// background services to resolve parked records.
    /// </summary>
    /// <typeparam name="TModel">The model type of the parked record</typeparam>
    /// <param name="parkedRecord">The parked record to heal</param>
    /// <param name="flowActions">The Flow actions service for re-injection</param>
    /// <param name="healedModel">The resolved/healed model to re-inject into Flow</param>
    /// <param name="healingReason">Optional reason for the healing operation (for audit/logging)</param>
    /// <param name="correlationId">Optional correlation ID (defaults to parked record's correlation ID)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Task representing the healing operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null</exception>
    /// <exception cref="InvalidOperationException">Thrown when the parked record has no data to heal</exception>
    public static async Task HealAsync<TModel>(
        this ParkedRecord<TModel> parkedRecord,
        IFlowActions flowActions,
        TModel healedModel,
        string? healingReason = null,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parkedRecord);
        ArgumentNullException.ThrowIfNull(flowActions);
        ArgumentNullException.ThrowIfNull(healedModel);

        if (parkedRecord.Data == null)
            throw new InvalidOperationException($"Cannot heal parked record {parkedRecord.Id}: no data available");

        // Use the parked record's correlation ID if none provided
        var effectiveCorrelationId = correlationId ?? parkedRecord.CorrelationId ?? Guid.CreateVersion7().ToString();
        
        // TODO: Add healing metadata to the model (if possible) or pass via context
        // For now, we'll rely on Flow's logging and tracing for audit
        
        // Extract the model name from the type
        var modelName = typeof(TModel).Name.ToLowerInvariant();
        
        // Re-inject the healed model into Flow's intake pipeline
        await flowActions.SeedAsync(modelName, effectiveCorrelationId, healedModel, effectiveCorrelationId, ct);
        
        // Remove the parked record after successful healing
        await parkedRecord.Delete(ct);
    }

    /// <summary>
    /// Heals a parked record by merging additional resolved properties with the original model.
    /// This overload is convenient when you want to preserve most of the original data and 
    /// just update specific fields.
    /// </summary>
    /// <typeparam name="TModel">The model type of the parked record</typeparam>
    /// <param name="parkedRecord">The parked record to heal</param>
    /// <param name="flowActions">The Flow actions service for re-injection</param>
    /// <param name="resolvedProperties">Additional properties to merge with original model</param>
    /// <param name="healingReason">Optional reason for the healing operation</param>
    /// <param name="correlationId">Optional correlation ID</param>
    /// <param name="ct">Cancellation token</param>
    public static async Task HealAsync<TModel>(
        this ParkedRecord<TModel> parkedRecord,
        IFlowActions flowActions,
        object resolvedProperties,
        string? healingReason = null,
        string? correlationId = null,
        CancellationToken ct = default)
        where TModel : new()
    {
        ArgumentNullException.ThrowIfNull(parkedRecord);
        ArgumentNullException.ThrowIfNull(resolvedProperties);

        if (parkedRecord.Data == null)
            throw new InvalidOperationException($"Cannot heal parked record {parkedRecord.Id}: no data available");

        // Create a copy of the original model and update with resolved properties
        var healedModel = CopyModel(parkedRecord.Data);
        
        // Use reflection to update properties from resolvedProperties object
        var resolvedType = resolvedProperties.GetType();
        var modelType = typeof(TModel);
        foreach (var resolvedProp in resolvedType.GetProperties())
        {
            if (resolvedProp.CanRead)
            {
                // Find matching property in the model (case-insensitive)
                var modelProp = modelType.GetProperty(resolvedProp.Name, 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                
                if (modelProp != null && modelProp.CanWrite)
                {
                    var resolvedValue = resolvedProp.GetValue(resolvedProperties);
                    modelProp.SetValue(healedModel, resolvedValue);
                }
            }
        }
        
        await HealAsync(parkedRecord, flowActions, healedModel, healingReason, correlationId, ct);
    }
    
    /// <summary>
    /// Creates a copy of a model using serialization/deserialization.
    /// This ensures we don't modify the original parked record data.
    /// </summary>
    private static TModel CopyModel<TModel>(TModel original) where TModel : new()
    {
        if (original == null) return new TModel();
        
        // Use JSON serialization for deep copy
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(original, Koan.Core.Json.JsonDefaults.Settings);
        return Newtonsoft.Json.JsonConvert.DeserializeObject<TModel>(json, Koan.Core.Json.JsonDefaults.Settings) ?? new TModel();
    }
}