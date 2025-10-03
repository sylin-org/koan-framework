using System;
using System.Threading;

namespace Koan.Canon.Context;

/// <summary>
/// Provides adapter context information for Canon operations.
/// Automatically captures the current adapter identity for proper metadata preservation.
/// </summary>
public sealed class CanonContext
{
    private static readonly AsyncLocal<CanonContext> _current = new();
    
    /// <summary>
    /// Gets the current Canon context for the executing thread.
    /// </summary>
    public static CanonContext? Current => _current.Value;
    
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
    /// Creates a new Canon context with the specified identifiers.
    /// </summary>
    public CanonContext(string system, string adapter, string? source = null)
    {
        System = system ?? throw new ArgumentNullException(nameof(system));
        Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        Source = source;
    }
    
    /// <summary>
    /// Pushes a new Canon context onto the current thread's context stack.
    /// Returns a disposable that will restore the previous context when disposed.
    /// </summary>
    public static IDisposable Push(CanonContext context)
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
        private readonly CanonContext? _previous;
        private bool _disposed;
        
        public ContextScope(CanonContext? previous)
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

