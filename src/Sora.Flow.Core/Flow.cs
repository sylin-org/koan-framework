using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Sora.Flow;

/// <summary>
/// Beautiful, fluent API for Flow messaging and orchestration.
/// Provides messaging-first architecture with clean separation between sending and receiving.
/// </summary>
public static class Flow
{
    /// <summary>
    /// Fluent API for sending messages, commands, and entities.
    /// </summary>
    public static FlowOutbound Outbound => FlowOutbound.Instance;

    /// <summary>
    /// Fluent API for registering message handlers and orchestration logic.
    /// </summary>
    public static FlowInbound Inbound => FlowInbound.Instance;

    /// <summary>
    /// Access to Flow infrastructure components.
    /// </summary>
    public static class Infrastructure
    {
        /// <summary>
        /// Registry for Flow model types and metadata.
        /// </summary>
        public static class FlowRegistry
        {
            /// <summary>
            /// Resolve a model type by name.
            /// </summary>
            public static Type? ResolveModel(string model) => Sora.Flow.Infrastructure.FlowRegistry.ResolveModel(model);
            
            /// <summary>
            /// Get aggregation tags for a model type.
            /// </summary>
            public static string[] GetAggregationTags(Type modelType) => Sora.Flow.Infrastructure.FlowRegistry.GetAggregationTags(modelType);
            
            /// <summary>
            /// Get model name for a type.
            /// </summary>
            public static string GetModelName(Type modelType) => Sora.Flow.Infrastructure.FlowRegistry.GetModelName(modelType);
        }
    }

    // Convenience methods for direct access
    
    /// <summary>
    /// Send a named command with optional payload.
    /// Usage: Flow.Send("seed", payload).To("bms:simulator") or Flow.Send("seed").Broadcast()
    /// </summary>
    public static FlowSendBuilder Send(string command, object? payload = null) 
        => Outbound.Send(command, payload);

    /// <summary>
    /// Send a typed entity through the messaging system.
    /// Usage: Flow.Send(device).To("target") or Flow.Send(device).Broadcast()
    /// </summary>
    public static FlowSendBuilder<T> Send<T>(T entity) where T : class 
        => Outbound.Send(entity);

    /// <summary>
    /// Register a handler for typed messages.
    /// Usage: Flow.On<Device>(device => ProcessDevice(device))
    /// </summary>
    public static FlowHandlerBuilder On<T>(Func<T, Task> handler) where T : class
        => Inbound.On(handler);

    /// <summary>
    /// Register a handler for typed messages with cancellation support.
    /// Usage: Flow.On<Device>((device, ct) => ProcessDevice(device, ct))
    /// </summary>
    public static FlowHandlerBuilder On<T>(Func<T, CancellationToken, Task> handler) where T : class
        => Inbound.On(handler);

    /// <summary>
    /// Register a handler for named commands.
    /// Usage: Flow.On("seed", payload => HandleSeed(payload))
    /// </summary>
    public static FlowHandlerBuilder On(string command, Func<object?, Task> handler)
        => Inbound.On(command, handler);

    /// <summary>
    /// Register a handler for named commands with cancellation support.
    /// Usage: Flow.On("seed", (payload, ct) => HandleSeed(payload, ct))
    /// </summary>
    public static FlowHandlerBuilder On(string command, Func<object?, CancellationToken, Task> handler)
        => Inbound.On(command, handler);

    /// <summary>
    /// Semantic alias for command handling.
    /// Usage: Flow.OnCommand<ControlCommand>(cmd => ExecuteCommand(cmd))
    /// </summary>
    public static FlowHandlerBuilder OnCommand<T>(Func<T, Task> handler) where T : class
        => Inbound.OnCommand(handler);

    /// <summary>
    /// Semantic alias for command handling with cancellation.
    /// </summary>
    public static FlowHandlerBuilder OnCommand<T>(Func<T, CancellationToken, Task> handler) where T : class
        => Inbound.OnCommand(handler);

    /// <summary>
    /// Semantic alias for named command handling.
    /// Usage: Flow.OnCommand("seed", payload => HandleSeed(payload))
    /// </summary>
    public static FlowHandlerBuilder OnCommand(string command, Func<object?, Task> handler)
        => Inbound.OnCommand(command, handler);

    /// <summary>
    /// Semantic alias for event handling.
    /// Usage: Flow.OnEvent<DeviceStatusChanged>(evt => ProcessEvent(evt))
    /// </summary>
    public static FlowHandlerBuilder OnEvent<T>(Func<T, Task> handler) where T : class
        => Inbound.OnEvent(handler);

    /// <summary>
    /// Semantic alias for event handling with cancellation.
    /// </summary>
    public static FlowHandlerBuilder OnEvent<T>(Func<T, CancellationToken, Task> handler) where T : class
        => Inbound.OnEvent(handler);
}