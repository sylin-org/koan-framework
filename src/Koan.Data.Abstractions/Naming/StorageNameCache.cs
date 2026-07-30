namespace Koan.Data.Abstractions.Naming;

/// <summary>Bounded host-owned memo for compiled storage anchors and ambient-aware physical names.</summary>
public sealed class StorageNameCache
{
    private readonly Partition<NameKey> _names;
    private readonly Partition<AnchorKey> _anchors;

    public StorageNameCache(int entries)
    {
        if (entries <= 0) throw new ArgumentOutOfRangeException(nameof(entries));
        _names = new Partition<NameKey>(entries);
        _anchors = new Partition<AnchorKey>(entries);
    }

    internal string Resolve(NameKey key, Func<string> create) => _names.Resolve(key, create);

    internal string Anchor(AnchorKey key, Func<string> create) => _anchors.Resolve(key, create);

    private sealed class Partition<TKey>(int limit) where TKey : notnull
    {
        private readonly object _gate = new();
        private readonly Dictionary<TKey, Entry> _entries = new();
        private readonly LinkedList<TKey> _order = new();

        public string Resolve(TKey key, Func<string> create)
        {
            Entry entry;
            lock (_gate)
            {
                if (!_entries.TryGetValue(key, out entry!))
                {
                    if (_entries.Count >= limit)
                    {
                        var expired = _order.First!;
                        _order.RemoveFirst();
                        _entries.Remove(expired.Value);
                    }
                    var node = _order.AddLast(key);
                    entry = new Entry(
                        new Lazy<string>(create, LazyThreadSafetyMode.ExecutionAndPublication),
                        node);
                    _entries.Add(key, entry);
                }
            }
            try { return entry.Value.Value; }
            catch
            {
                lock (_gate)
                {
                    if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current.Value, entry.Value))
                    {
                        _entries.Remove(key);
                        _order.Remove(current.Node);
                    }
                }
                throw;
            }
        }

        private sealed record Entry(Lazy<string> Value, LinkedListNode<TKey> Node);
    }

    internal readonly record struct NameKey(
        string Provider,
        Type Entity,
        string? Partition,
        string Axis,
        string? Source);

    internal readonly record struct AnchorKey(string Provider, Type Entity);
}
