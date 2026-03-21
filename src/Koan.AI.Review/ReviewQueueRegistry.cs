using System.Collections.Concurrent;

namespace Koan.AI.Review;

/// <summary>
/// Central registry for review queues. Queues are registered at startup and resolved
/// by name at runtime for the human-in-the-loop review flow.
/// </summary>
public sealed class ReviewQueueRegistry
{
    private readonly ConcurrentDictionary<string, object> _queues = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Register a typed review queue.</summary>
    public void Register<T>(ReviewQueue<T> queue) where T : IReviewable
    {
        if (!_queues.TryAdd(queue.Name, queue))
        {
            throw new InvalidOperationException(
                $"A review queue named '{queue.Name}' is already registered.");
        }
    }

    /// <summary>Retrieve a typed review queue by name.</summary>
    public ReviewQueue<T>? Get<T>(string name) where T : IReviewable
    {
        return _queues.TryGetValue(name, out var queue) ? queue as ReviewQueue<T> : null;
    }

    /// <summary>List all registered queue names.</summary>
    public IReadOnlyList<string> Names => _queues.Keys.ToList().AsReadOnly();
}
