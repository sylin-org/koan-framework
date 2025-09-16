using System;
using System.Threading;

namespace Koan.Flow.Context;

/// <summary>
/// Provides adapter context information for Flow operations.
/// Automatically captures the current adapter identity for proper metadata preservation.
/// </summary>
public sealed class FlowContext
{
    private static readonly AsyncLocal<FlowContext> _current = new();
    
    /// <summary>
    /// Gets the current flow context for the executing thread.
    /// </summary>
    public static FlowContext? Current => _current.Value;
    
    /// <summary>
    /// The system identifier (e.g., "bms", "oem").
    /// </summary>
    public string System { get; init; } = string.Empty;
    
    /// <summary>
    /// The adapter identifier (e.g., "bms", "oem").
    /// </summary>
    public string Adapter { get; init; } = string.Empty;
    
    /// <summary>
    /// Optional source identifier for more specific tracking.
    /// </summary>
    public string? Source { get; init; }
    
    /// <summary>
    /// Creates a new flow context with the specified identifiers.
    /// </summary>
    public FlowContext(string system, string adapter, string? source = null)
    {
        System = system ?? throw new ArgumentNullException(nameof(system));
        Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        Source = source;
    }
    
    /// <summary>
    /// Pushes a new flow context onto the current thread's context stack.
    /// Returns a disposable that will restore the previous context when disposed.
    /// </summary>
    public static IDisposable Push(FlowContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        
        var previous = _current.Value;
        _current.Value = context;
        return new ContextScope(previous);
    }
    
    /// <summary>
    /// Gets the effective source identifier, falling back to system:adapter if Source is null.
    /// </summary>
    public string GetEffectiveSource()
    {
        return Source ?? $"{System}:{Adapter}";
    }
    
    private sealed class ContextScope : IDisposable
    {
        private readonly FlowContext? _previous;
        private bool _disposed;
        
        public ContextScope(FlowContext? previous)
        {
            _previous = previous;
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                _current.Value = _previous;
                _disposed = true;
            }
        }
    }
}