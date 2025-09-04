using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Sora.Messaging;

namespace Sora.Flow.Configuration;

/// <summary>
/// Service collection extensions for configuring Flow message handlers properly.
/// </summary>
public static class FlowServiceExtensions
{
    /// <summary>
    /// Configure Flow message handlers during service registration.
    /// Usage: services.ConfigureFlow(flow => flow.On<Device>(device => ProcessDevice(device)))
    /// </summary>
    public static IServiceCollection ConfigureFlow(this IServiceCollection services, Action<FlowHandlerConfigurator> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        
        var configurator = new FlowHandlerConfigurator(services);
        configure(configurator);
        
        return services;
    }
}

/// <summary>
/// Fluent configurator for Flow message handlers that integrates with DI properly.
/// </summary>
public sealed class FlowHandlerConfigurator
{
    private readonly IServiceCollection _services;

    internal FlowHandlerConfigurator(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Register a handler for typed messages.
    /// Usage: flow.On<Device>(device => ProcessDevice(device))
    /// </summary>
    public FlowHandlerConfigurator On<T>(Func<T, Task> handler) where T : class
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
    /// Register a handler for typed messages with cancellation support.
    /// </summary>
    public FlowHandlerConfigurator On<T>(Func<T, CancellationToken, Task> handler) where T : class
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
    /// Register a handler for named commands.
    /// Usage: flow.On("seed", payload => HandleSeed(payload))
    /// </summary>
    public FlowHandlerConfigurator On(string command, Func<object?, Task> handler)
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
    /// Register a handler for named commands with cancellation support.
    /// </summary>
    public FlowHandlerConfigurator On(string command, Func<object?, CancellationToken, Task> handler)
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

    // Semantic aliases for clarity
    public FlowHandlerConfigurator OnCommand<T>(Func<T, Task> handler) where T : class => On(handler);
    public FlowHandlerConfigurator OnCommand<T>(Func<T, CancellationToken, Task> handler) where T : class => On(handler);
    public FlowHandlerConfigurator OnCommand(string command, Func<object?, Task> handler) => On(command, handler);
    public FlowHandlerConfigurator OnEvent<T>(Func<T, Task> handler) where T : class => On(handler);
    public FlowHandlerConfigurator OnEvent<T>(Func<T, CancellationToken, Task> handler) where T : class => On(handler);

    private static bool ShouldProcessMessage(string? target)
    {
        // If no target specified, it's a broadcast - process it
        if (string.IsNullOrWhiteSpace(target)) return true;
        
        // TODO: Implement adapter identity matching
        return true;
    }
}