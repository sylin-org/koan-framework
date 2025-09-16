using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Koan.Data.Core;
using Koan.Flow.Model;
using Koan.Data.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Koan.Flow.Core.Orchestration;

/// <summary>
/// Delegate for orchestrator update handlers with reference modification support.
/// Since C# doesn't allow ref parameters in async methods, this delegate is synchronous 
/// but returns a Task for async operations.
/// </summary>
public delegate Task<UpdateResult> UpdateHandler<T>(ref T proposed, T? current, UpdateMetadata metadata) where T : IEntity<string>;

/// <summary>
/// Contains metadata about the update being processed.
/// </summary>
public class UpdateMetadata
{
    public string SourceSystem { get; set; } = string.Empty;
    public string SourceAdapter { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, object> Properties { get; set; } = new();
}

/// <summary>
/// Result of an orchestrator update decision.
/// </summary>
public class UpdateResult
{
    public UpdateAction Action { get; set; }
    public string? Reason { get; set; }
    public TimeSpan? RetryAfter { get; set; }
}

/// <summary>
/// Action to take based on orchestrator decision.
/// </summary>
public enum UpdateAction
{
    Continue,
    Skip,
    Defer
}

/// <summary>
/// Static helper for orchestrator decisions.
/// </summary>
public static class Update
{
    public static UpdateResult Continue(string? reason = null) =>
        new() { Action = UpdateAction.Continue, Reason = reason };

    public static UpdateResult Skip(string reason) =>
        new() { Action = UpdateAction.Skip, Reason = reason };

    public static UpdateResult Defer(string reason, TimeSpan? retryAfter = null) =>
        new() { Action = UpdateAction.Defer, Reason = reason, RetryAfter = retryAfter };
}

/// <summary>
/// Static helper for Flow orchestrator patterns.
/// </summary>
public static class Flow
{
    private static readonly Dictionary<Type, object> _handlers = new();

    /// <summary>
    /// Register an update handler for a specific entity type.
    /// </summary>
    public static void OnUpdate<T>(UpdateHandler<T> handler) where T : class, IEntity<string>
    {
        _handlers[typeof(T)] = handler;
    }

    /// <summary>
    /// Get the registered handler for a specific entity type.
    /// </summary>
    internal static UpdateHandler<T>? GetHandler<T>() where T : class, IEntity<string>
    {
        return _handlers.TryGetValue(typeof(T), out var handler) ? (UpdateHandler<T>)handler : null;
    }

    /// <summary>
    /// Check if a handler is registered for a specific entity type.
    /// </summary>
    internal static bool HasHandler<T>() where T : class, IEntity<string>
    {
        return _handlers.ContainsKey(typeof(T));
    }

    /// <summary>
    /// Check if a handler is registered for a specific type.
    /// </summary>
    internal static bool HasHandler(Type type)
    {
        return _handlers.ContainsKey(type);
    }

    /// <summary>
    /// Get the registered handler for a specific type.
    /// </summary>
    internal static object? GetHandler(Type type)
    {
        return _handlers.TryGetValue(type, out var handler) ? handler : null;
    }
}