using System.Collections.Concurrent;

namespace Koan.Context.Services;

/// <summary>
/// Batches and debounces file change events to prevent excessive re-indexing
/// </summary>
public class DebouncingQueue
{
    private readonly int _debounceMilliseconds;
    private readonly Func<List<FileChange>, Task> _processAction;
    private readonly ConcurrentDictionary<string, FileChange> _pending = new();
    private Timer? _debounceTimer;
    private readonly object _lock = new();

    public DebouncingQueue(int debounceMilliseconds, Func<List<FileChange>, Task> processAction)
    {
        _debounceMilliseconds = debounceMilliseconds;
        _processAction = processAction;
    }

    /// <summary>
    /// Enqueues a file change, resetting the debounce timer
    /// </summary>
    public void Enqueue(FileChange change)
    {
        // Deduplicate by path (keep latest change)
        _pending[change.Path] = change;

        lock (_lock)
        {
            // Reset debounce timer
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(
                async _ => await FlushAsync(),
                null,
                _debounceMilliseconds,
                Timeout.Infinite);
        }
    }

    /// <summary>
    /// Flushes all pending changes to the process action
    /// </summary>
    private async Task FlushAsync()
    {
        if (_pending.IsEmpty) return;

        // Extract all pending changes
        var changes = _pending.Values.ToList();
        _pending.Clear();

        // Process batched changes
        try
        {
            await _processAction(changes);
        }
        catch
        {
            // Swallow exceptions to prevent timer crashes
        }
    }
}

/// <summary>
/// Represents a file system change event
/// </summary>
public record FileChange
{
    public required string Path { get; init; }
    public required FileChangeType Type { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Type of file system change
/// </summary>
public enum FileChangeType
{
    Modified,
    Deleted,
    Renamed
}
