using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Sora.Core.Hosting.App;
using Sora.Messaging;

namespace Sora.Flow;

/// <summary>
/// Fluent API for registering message handlers in the Flow system.
/// Provides beautiful, chainable syntax for orchestrator message handling.
/// </summary>
public sealed class FlowInbound
{
    internal static readonly FlowInbound Instance = new();
    private FlowInbound() { }

    /// <summary>
    /// Register a handler for typed messages.
    /// Usage: Flow.Inbound.On<Device>(device => ProcessDevice(device))
    /// </summary>
    public FlowHandlerBuilder On<T>(Func<T, Task> handler) where T : class
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        
        var services = ResolveServiceCollection();
        services.On<FlowTargetedMessage<T>>(async msg =>
        {
            // Apply targeting filter if needed
            if (ShouldProcessMessage(msg.Target))
            {
                await handler(msg.Entity);
            }
        });
        
        return new FlowHandlerBuilder(services);
    }

    /// <summary>
    /// Register a handler for typed messages with cancellation support.
    /// </summary>
    public FlowHandlerBuilder On<T>(Func<T, CancellationToken, Task> handler) where T : class
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        
        var services = ResolveServiceCollection();
        services.On<FlowTargetedMessage<T>>(async msg =>
        {
            if (ShouldProcessMessage(msg.Target))
            {
                await handler(msg.Entity, CancellationToken.None);
            }
        });
        
        return new FlowHandlerBuilder(services);
    }

    /// <summary>
    /// Register a handler for named commands.
    /// Usage: Flow.Inbound.On("seed", payload => HandleSeed(payload))
    /// </summary>
    public FlowHandlerBuilder On(string command, Func<object?, Task> handler)
    {
        if (string.IsNullOrWhiteSpace(command)) throw new ArgumentException("Command cannot be null or empty", nameof(command));
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        
        var services = ResolveServiceCollection();
        services.On<FlowCommandMessage>(async msg =>
        {
            if (string.Equals(msg.Command, command, StringComparison.OrdinalIgnoreCase) &&
                ShouldProcessMessage(msg.Target))
            {
                await handler(msg.Payload);
            }
        });
        
        return new FlowHandlerBuilder(services);
    }

    /// <summary>
    /// Register a handler for named commands with cancellation support.
    /// </summary>
    public FlowHandlerBuilder On(string command, Func<object?, CancellationToken, Task> handler)
    {
        if (string.IsNullOrWhiteSpace(command)) throw new ArgumentException("Command cannot be null or empty", nameof(command));
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        
        var services = ResolveServiceCollection();
        services.On<FlowCommandMessage>(async msg =>
        {
            if (string.Equals(msg.Command, command, StringComparison.OrdinalIgnoreCase) &&
                ShouldProcessMessage(msg.Target))
            {
                await handler(msg.Payload, CancellationToken.None);
            }
        });
        
        return new FlowHandlerBuilder(services);
    }

    /// <summary>
    /// Semantic alias for command handling.
    /// </summary>
    public FlowHandlerBuilder OnCommand<T>(Func<T, Task> handler) where T : class
        => On(handler);

    /// <summary>
    /// Semantic alias for command handling with cancellation.
    /// </summary>
    public FlowHandlerBuilder OnCommand<T>(Func<T, CancellationToken, Task> handler) where T : class
        => On(handler);

    /// <summary>
    /// Semantic alias for named command handling.
    /// </summary>
    public FlowHandlerBuilder OnCommand(string command, Func<object?, Task> handler)
        => On(command, handler);

    /// <summary>
    /// Semantic alias for named command handling with cancellation.
    /// </summary>
    public FlowHandlerBuilder OnCommand(string command, Func<object?, CancellationToken, Task> handler)
        => On(command, handler);

    /// <summary>
    /// Semantic alias for event handling.
    /// </summary>
    public FlowHandlerBuilder OnEvent<T>(Func<T, Task> handler) where T : class
        => On(handler);

    /// <summary>
    /// Semantic alias for event handling with cancellation.
    /// </summary>
    public FlowHandlerBuilder OnEvent<T>(Func<T, CancellationToken, Task> handler) where T : class
        => On(handler);

    private static IServiceCollection ResolveServiceCollection()
    {
        // This is called during static initialization - we need a different approach
        // The handlers should be registered via the orchestrator auto-registration system
        // For now, we'll create a deferred registration system
        throw new InvalidOperationException(
            "Flow.On<T>() must be called during service registration or in a FlowOrchestrator context. " +
            "Use services.ConfigureFlow(flow => flow.On<T>(...)) instead.");
    }

    private static bool ShouldProcessMessage(string? target)
    {
        // If no target specified, it's a broadcast - process it
        if (string.IsNullOrWhiteSpace(target)) return true;
        
        // TODO: Implement adapter identity matching
        // This would check if the current service matches the target
        // For now, process all targeted messages
        return true;
    }
}

/// <summary>
/// Fluent builder for chaining multiple Flow message handlers.
/// Provides beautiful, chainable syntax for registering handlers.
/// </summary>
public sealed class FlowHandlerBuilder
{
    private readonly IServiceCollection _services;

    internal FlowHandlerBuilder(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Chain registration of another typed message handler.
    /// Usage: builder.On<Device>(...).On<Reading>(...)
    /// </summary>
    public FlowHandlerBuilder On<T>(Func<T, Task> handler) where T : class
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        
        _services.On<FlowTargetedMessage<T>>(async msg =>
        {
            if (ShouldProcessMessage(msg.Target))
            {
                await handler(msg.Entity);
            }
        });
        
        return this;
    }

    /// <summary>
    /// Chain registration of another typed message handler with cancellation.
    /// </summary>
    public FlowHandlerBuilder On<T>(Func<T, CancellationToken, Task> handler) where T : class
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        
        _services.On<FlowTargetedMessage<T>>(async msg =>
        {
            if (ShouldProcessMessage(msg.Target))
            {
                await handler(msg.Entity, CancellationToken.None);
            }
        });
        
        return this;
    }

    /// <summary>
    /// Chain registration of a named command handler.
    /// Usage: builder.On<Device>(...).On("seed", ...)
    /// </summary>
    public FlowHandlerBuilder On(string command, Func<object?, Task> handler)
    {
        if (string.IsNullOrWhiteSpace(command)) throw new ArgumentException("Command cannot be null or empty", nameof(command));
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        
        _services.On<FlowCommandMessage>(async msg =>
        {
            if (string.Equals(msg.Command, command, StringComparison.OrdinalIgnoreCase) &&
                ShouldProcessMessage(msg.Target))
            {
                await handler(msg.Payload);
            }
        });
        
        return this;
    }

    /// <summary>
    /// Chain registration of a named command handler with cancellation.
    /// </summary>
    public FlowHandlerBuilder On(string command, Func<object?, CancellationToken, Task> handler)
    {
        if (string.IsNullOrWhiteSpace(command)) throw new ArgumentException("Command cannot be null or empty", nameof(command));
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        
        _services.On<FlowCommandMessage>(async msg =>
        {
            if (string.Equals(msg.Command, command, StringComparison.OrdinalIgnoreCase) &&
                ShouldProcessMessage(msg.Target))
            {
                await handler(msg.Payload, CancellationToken.None);
            }
        });
        
        return this;
    }

    // Semantic aliases for fluent chaining
    
    public FlowHandlerBuilder OnCommand<T>(Func<T, Task> handler) where T : class => On(handler);
    public FlowHandlerBuilder OnCommand<T>(Func<T, CancellationToken, Task> handler) where T : class => On(handler);
    public FlowHandlerBuilder OnCommand(string command, Func<object?, Task> handler) => On(command, handler);
    public FlowHandlerBuilder OnCommand(string command, Func<object?, CancellationToken, Task> handler) => On(command, handler);
    public FlowHandlerBuilder OnEvent<T>(Func<T, Task> handler) where T : class => On(handler);
    public FlowHandlerBuilder OnEvent<T>(Func<T, CancellationToken, Task> handler) where T : class => On(handler);

    private static bool ShouldProcessMessage(string? target)
    {
        // If no target specified, it's a broadcast - process it
        if (string.IsNullOrWhiteSpace(target)) return true;
        
        // TODO: Implement adapter identity matching
        return true;
    }
}