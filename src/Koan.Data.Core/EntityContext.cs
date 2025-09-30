using System;
using System.Threading;

namespace Koan.Data.Core;

/// <summary>
/// Ambient routing context for entity operations.
/// Supports source OR adapter selection (mutually exclusive) plus partition routing.
///
/// Routing dimensions:
/// - Source: Named configuration (e.g., "analytics", "backup") - sources define their own adapter
/// - Adapter: Provider override (e.g., "sqlite", "postgres") - used with default source
/// - Partition: Storage partition suffix (e.g., "archive", "cold") - appended to storage name
///
/// Source and Adapter are mutually exclusive - specifying both throws InvalidOperationException.
/// </summary>
public static class EntityContext
{
    private static readonly AsyncLocal<ContextState?> _current = new();

    /// <summary>
    /// Routing context state combining source, adapter, and partition.
    /// </summary>
    public sealed record ContextState
    {
        public string? Source { get; init; }
        public string? Adapter { get; init; }
        public string? Partition { get; init; }

        /// <summary>
        /// Create routing context with validation.
        /// </summary>
        /// <param name="source">Named source configuration (e.g., "analytics")</param>
        /// <param name="adapter">Adapter override (e.g., "sqlite")</param>
        /// <param name="partition">Storage partition suffix (e.g., "archive")</param>
        /// <exception cref="InvalidOperationException">Thrown when both source and adapter are specified</exception>
        public ContextState(string? source = null, string? adapter = null, string? partition = null)
        {
            // Critical constraint: source and adapter are mutually exclusive
            if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(adapter))
                throw new InvalidOperationException(
                    "Cannot specify both 'source' and 'adapter'. Sources define their own adapter selection.");

            Source = source;
            Adapter = adapter;
            Partition = partition;
        }

        internal void ValidatePartitionName()
        {
            if (string.IsNullOrWhiteSpace(Partition)) return;

            if (!PartitionNameValidator.IsValid(Partition))
                throw new ArgumentException(
                    $"Invalid partition name '{Partition}'. " +
                    $"Must start with letter, contain only alphanumeric characters, '-' or '.', " +
                    $"and not end with '.' or '-'.",
                    nameof(Partition));
        }
    }

    /// <summary>
    /// Get current routing context (null if not set).
    /// </summary>
    public static ContextState? Current => _current.Value;

    /// <summary>
    /// Set routing context. Replaces any previous context (does not merge).
    /// </summary>
    /// <param name="source">Named source configuration</param>
    /// <param name="adapter">Adapter override</param>
    /// <param name="partition">Storage partition suffix</param>
    /// <returns>Disposable that restores previous context on disposal</returns>
    /// <exception cref="InvalidOperationException">Thrown when both source and adapter are specified</exception>
    /// <exception cref="ArgumentException">Thrown when partition name is invalid</exception>
    public static IDisposable With(string? source = null, string? adapter = null, string? partition = null)
    {
        var prev = _current.Value;
        var newContext = new ContextState(source, adapter, partition);
        newContext.ValidatePartitionName();

        _current.Value = newContext;
        return new Pop(() => _current.Value = prev);
    }

    /// <summary>
    /// Convenience method to set only source routing.
    /// </summary>
    public static IDisposable Source(string source) => With(source: source);

    /// <summary>
    /// Convenience method to set only adapter routing.
    /// </summary>
    public static IDisposable Adapter(string adapter) => With(adapter: adapter);

    /// <summary>
    /// Convenience method to set only partition routing.
    /// </summary>
    public static IDisposable Partition(string partition) => With(partition: partition);

    private sealed class Pop(Action action) : IDisposable
    {
        private Action? _action = action;
        public void Dispose()
        {
            _action?.Invoke();
            _action = null;
        }
    }
}
