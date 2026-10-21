namespace Koan.Data.Relational.Mapping;

/// <summary>
/// A repository's compiled plans, one per physical container (a partition's table), holding at most a bound and dropping
/// the least recently used plan when a new one is admitted (NK-170). A plan is recomputable state, so dropping one costs
/// a recompile on that container's next use, never a failure: an application with a partition per customer or per day
/// keeps working past the bound. Creation runs under the cache's lock, so one container is compiled once at a time.
/// </summary>
public sealed class RelationalPlanCache<TPlan> where TPlan : class
{
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, TPlan Plan)>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, TPlan Plan)> _recency = new();
    private readonly int _capacity;

    public RelationalPlanCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    /// <summary>The number of plans held now.</summary>
    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    /// <summary>The plan for <paramref name="key"/>, created by <paramref name="create"/> when it is not held.</summary>
    public TPlan GetOrAdd(string key, Func<string, TPlan> create)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(create);
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var held))
            {
                if (!ReferenceEquals(_recency.First, held))
                {
                    _recency.Remove(held);
                    _recency.AddFirst(held);
                }
                return held.Value.Plan;
            }

            var plan = create(key);
            if (_entries.Count >= _capacity && _recency.Last is { } oldest)
            {
                _recency.RemoveLast();
                _entries.Remove(oldest.Value.Key);
            }
            _entries.Add(key, _recency.AddFirst((key, plan)));
            return plan;
        }
    }
}
