using System;
using System.Threading;
using System.Threading.Tasks;
using Sora.Core.Hosting.App;
using Sora.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Sora.Flow;

/// <summary>
/// Fluent API for sending messages through the Flow messaging system.
/// Handles both typed entities and named commands with targeting and broadcasting.
/// </summary>
public sealed class FlowOutbound
{
    internal static readonly FlowOutbound Instance = new();
    private FlowOutbound() { }

    /// <summary>
    /// Send a named command with optional payload.
    /// Returns a builder for fluent targeting (.To() or .Broadcast()).
    /// </summary>
    public FlowSendBuilder Send(string command, object? payload = null)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Command cannot be null or empty", nameof(command));
        
        return new FlowSendBuilder(command, payload);
    }

    /// <summary>
    /// Send a typed entity through the messaging system.
    /// Returns a builder for fluent targeting (.To() or .Broadcast()).
    /// </summary>
    public FlowSendBuilder<T> Send<T>(T entity) where T : class
    {
        if (entity is null)
            throw new ArgumentNullException(nameof(entity));
            
        return new FlowSendBuilder<T>(entity);
    }
}

/// <summary>
/// Fluent builder for sending named commands with targeting.
/// </summary>
public sealed class FlowSendBuilder
{
    private readonly string _command;
    private readonly object? _payload;

    internal FlowSendBuilder(string command, object? payload)
    {
        _command = command;
        _payload = payload;
    }

    /// <summary>
    /// Send the command to a specific target adapter.
    /// Target format: "system:adapter" (e.g., "bms:simulator")
    /// </summary>
    public Task To(string target, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(target))
            throw new ArgumentException("Target cannot be null or empty", nameof(target));

        return SendWithTarget(target, ct);
    }

    /// <summary>
    /// Broadcast the command to all registered handlers.
    /// </summary>
    public Task Broadcast(CancellationToken ct = default)
    {
        return SendWithTarget(null, ct);
    }

    /// <summary>
    /// Default send behavior (broadcast).
    /// </summary>
    public Task Send(CancellationToken ct = default)
    {
        return Broadcast(ct);
    }

    private async Task SendWithTarget(string? target, CancellationToken ct)
    {
        try
        {
            var messageProxy = ResolveMessageProxy();
            
            // Create command message with targeting information
            var commandMessage = new FlowCommandMessage
            {
                Command = _command,
                Payload = _payload,
                Target = target,
                Timestamp = DateTimeOffset.UtcNow
            };

            await messageProxy.SendAsync(commandMessage, ct);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            throw new InvalidOperationException(
                $"Failed to send Flow command '{_command}' via messaging system. " +
                $"Target: {target ?? "(broadcast)"}. " +
                $"Ensure messaging provider is configured and available.", ex);
        }
    }

    private static IMessageProxy ResolveMessageProxy()
    {
        var serviceProvider = AppHost.Current 
            ?? throw new InvalidOperationException("AppHost.Current is not initialized. Ensure AddSora() was called during startup.");
            
        var messageProxy = serviceProvider.GetService<IMessageProxy>()
            ?? throw new InvalidOperationException("IMessageProxy is not registered. Ensure AddSora() and messaging core services are configured.");
            
        return messageProxy;
    }
}

/// <summary>
/// Fluent builder for sending typed entities with targeting.
/// </summary>
public sealed class FlowSendBuilder<T> where T : class
{
    private readonly T _entity;

    internal FlowSendBuilder(T entity)
    {
        _entity = entity;
    }

    /// <summary>
    /// Send the entity to a specific target adapter.
    /// Target format: "system:adapter" (e.g., "bms:simulator")
    /// </summary>
    public Task To(string target, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(target))
            throw new ArgumentException("Target cannot be null or empty", nameof(target));

        return SendWithTarget(target, ct);
    }

    /// <summary>
    /// Broadcast the entity to all registered handlers.
    /// </summary>
    public Task Broadcast(CancellationToken ct = default)
    {
        return SendWithTarget(null, ct);
    }

    /// <summary>
    /// Default send behavior (broadcast).
    /// </summary>
    public Task Send(CancellationToken ct = default)
    {
        return Broadcast(ct);
    }

    private async Task SendWithTarget(string? target, CancellationToken ct)
    {
        try
        {
            var messageProxy = ResolveMessageProxy();
            
            // Create targeted message wrapper
            var targetedMessage = new FlowTargetedMessage<T>
            {
                Entity = _entity,
                Target = target,
                Timestamp = DateTimeOffset.UtcNow
            };

            await messageProxy.SendAsync(targetedMessage, ct);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            throw new InvalidOperationException(
                $"Failed to send {typeof(T).Name} entity via messaging system. " +
                $"Target: {target ?? "(broadcast)"}. " +
                $"Ensure messaging provider is configured and available.", ex);
        }
    }

    private static IMessageProxy ResolveMessageProxy()
    {
        var serviceProvider = AppHost.Current 
            ?? throw new InvalidOperationException("AppHost.Current is not initialized. Ensure AddSora() was called during startup.");
            
        var messageProxy = serviceProvider.GetService<IMessageProxy>()
            ?? throw new InvalidOperationException("IMessageProxy is not registered. Ensure AddSora() and messaging core services are configured.");
            
        return messageProxy;
    }
}

/// <summary>
/// Message wrapper for named commands with targeting.
/// </summary>
// Removed Message attribute - new messaging system doesn't use attributes
public sealed class FlowCommandMessage
{
    public string Command { get; set; } = default!;
    public object? Payload { get; set; }
    public string? Target { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>
/// Message wrapper for typed entities with targeting.
/// </summary>
// Removed Message attribute - new messaging system doesn't use attributes
public sealed class FlowTargetedMessage<T> where T : class
{
    public T Entity { get; set; } = default!;
    public string? Target { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}