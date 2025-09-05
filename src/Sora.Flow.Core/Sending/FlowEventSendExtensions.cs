using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Sora.Core.Hosting.App;
using Sora.Messaging;
using Sora.Flow.Infrastructure;

namespace Sora.Flow.Sending;

/// <summary>
/// Extensions to send FlowEvent through the new messaging system.
/// Provides a bridge from legacy FlowEvent pattern to messaging-first architecture.
/// </summary>
public static class FlowEventSendExtensions
{
    /// <summary>
    /// Send a FlowEvent through the messaging system as a broadcast.
    /// The event will be routed to the orchestrator for processing.
    /// </summary>
    public static async Task Send(this FlowEvent flowEvent, CancellationToken ct = default)
    {
        if (flowEvent is null) throw new ArgumentNullException(nameof(flowEvent));
        
        // Ensure messaging system is available
        var sp = AppHost.Current;
        if (sp is null) throw new InvalidOperationException("AppHost.Current is not initialized.");
        
        // If we have a model hint, try to resolve the actual type and use typed messaging
        if (!string.IsNullOrWhiteSpace(flowEvent.Model))
        {
            var modelType = FlowRegistry.ResolveModel(flowEvent.Model);
            if (modelType != null)
            {
                // Create an instance of the model from the bag data
                var instance = CreateModelInstance(modelType, flowEvent.Bag);
                if (instance != null)
                {
                    // Use the typed messaging pattern
                    await SendTypedModel(instance, modelType, ct);
                    return;
                }
            }
        }
        
        // Fallback: Send as a generic FlowEvent message
        await Flow.Send(flowEvent).Broadcast(ct);
    }
    
    /// <summary>
    /// Send a FlowEvent to a specific target through messaging.
    /// Target format: "system:adapter" (e.g., "bms:simulator")
    /// </summary>
    public static async Task SendTo(this FlowEvent flowEvent, string target, CancellationToken ct = default)
    {
        if (flowEvent is null) throw new ArgumentNullException(nameof(flowEvent));
        if (string.IsNullOrWhiteSpace(target)) throw new ArgumentNullException(nameof(target));
        
        // If we have a model hint, try to resolve the actual type
        if (!string.IsNullOrWhiteSpace(flowEvent.Model))
        {
            var modelType = FlowRegistry.ResolveModel(flowEvent.Model);
            if (modelType != null)
            {
                var instance = CreateModelInstance(modelType, flowEvent.Bag);
                if (instance != null)
                {
                    await SendTypedModelTo(instance, modelType, target, ct);
                    return;
                }
            }
        }
        
        // Fallback: Send as generic FlowEvent
        await Flow.Send(flowEvent).To(target, ct);
    }
    
    /// <summary>
    /// Send multiple FlowEvents through the messaging system.
    /// </summary>
    public static async Task Send(this IEnumerable<FlowEvent> flowEvents, CancellationToken ct = default)
    {
        if (flowEvents is null) return;
        var tasks = flowEvents.Select(e => e.Send(ct));
        await Task.WhenAll(tasks);
    }
    
    /// <summary>
    /// Send multiple FlowEvents to a specific target.
    /// </summary>
    public static async Task SendTo(this IEnumerable<FlowEvent> flowEvents, string target, CancellationToken ct = default)
    {
        if (flowEvents is null) return;
        var tasks = flowEvents.Select(e => e.SendTo(target, ct));
        await Task.WhenAll(tasks);
    }
    
    private static object? CreateModelInstance(Type modelType, Dictionary<string, object?> bag)
    {
        try
        {
            var instance = Activator.CreateInstance(modelType);
            if (instance == null) return null;
            
            // Map bag values to properties
            foreach (var prop in modelType.GetProperties())
            {
                if (!prop.CanWrite) continue;
                
                // Try exact match
                if (bag.TryGetValue(prop.Name, out var value))
                {
                    SetPropertyValue(instance, prop, value);
                    continue;
                }
                
                // Try model.* prefixed match
                var modelKey = $"{Constants.Reserved.ModelPrefix}{prop.Name}";
                if (bag.TryGetValue(modelKey, out value))
                {
                    SetPropertyValue(instance, prop, value);
                }
            }
            
            return instance;
        }
        catch
        {
            return null;
        }
    }
    
    private static void SetPropertyValue(object instance, System.Reflection.PropertyInfo prop, object? value)
    {
        try
        {
            if (value == null)
            {
                prop.SetValue(instance, null);
                return;
            }
            
            var targetType = prop.PropertyType;
            var valueType = value.GetType();
            
            // Direct assignment if types match
            if (targetType.IsAssignableFrom(valueType))
            {
                prop.SetValue(instance, value);
                return;
            }
            
            // Convert if possible
            if (targetType == typeof(string))
            {
                prop.SetValue(instance, value.ToString());
            }
            else if (targetType.IsEnum && value is string strValue)
            {
                var enumValue = Enum.Parse(targetType, strValue, true);
                prop.SetValue(instance, enumValue);
            }
            else
            {
                var converted = Convert.ChangeType(value, targetType);
                prop.SetValue(instance, converted);
            }
        }
        catch
        {
            // Skip properties that can't be set
        }
    }
    
    private static async Task SendTypedModel(object instance, Type modelType, CancellationToken ct)
    {
        // Use reflection to call the appropriate Send extension based on model type
        var sendMethod = typeof(FlowEntitySendExtensions)
            .GetMethods()
            .FirstOrDefault(m => m.Name == "Send" && 
                m.IsGenericMethodDefinition && 
                m.GetParameters().Length == 2);
        
        if (sendMethod != null)
        {
            var genericMethod = sendMethod.MakeGenericMethod(modelType);
            await (Task)genericMethod.Invoke(null, new object[] { instance, ct })!;
        }
        else
        {
            // Fallback to direct messaging
            await Flow.Send(instance).Broadcast(ct);
        }
    }
    
    private static async Task SendTypedModelTo(object instance, Type modelType, string target, CancellationToken ct)
    {
        // Use reflection to call the appropriate SendTo extension
        var sendMethod = typeof(FlowEntitySendExtensions)
            .GetMethods()
            .FirstOrDefault(m => m.Name == "SendTo" && 
                m.IsGenericMethodDefinition && 
                m.GetParameters().Length == 3);
        
        if (sendMethod != null)
        {
            var genericMethod = sendMethod.MakeGenericMethod(modelType);
            await (Task)genericMethod.Invoke(null, new object[] { instance, target, ct })!;
        }
        else
        {
            // Fallback to direct messaging
            await Flow.Send(instance).To(target, ct);
        }
    }
}