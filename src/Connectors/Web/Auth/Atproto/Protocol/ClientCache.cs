namespace Koan.Web.Auth.Connector.Atproto.Protocol;

/// <summary>
/// A bounded cache of disposable clients, one per key, that evicts and disposes. Admitting a key past capacity evicts
/// the entry used least recently; it never empties itself and never refuses. A client is rented for the length of one
/// operation, and one that is evicted, replaced or retired while rented is disposed when its last renter returns it,
/// never under one. Its capacity sizes memory only.
/// </summary>
internal sealed class ClientCache<TClient>(int capacity) where TClient : class, IDisposable
{
    private readonly int capacity = capacity >= 1 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly object gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> recency = new();

    /// <summary>How many clients the cache holds now.</summary>
    public int Count
    {
        get { lock (gate) return entries.Count; }
    }

    /// <summary>
    /// Rents <paramref name="key"/>'s client when <paramref name="fits"/> accepts it. One it does not accept is retired
    /// and null returned, so the caller builds and admits another.
    /// </summary>
    public Lease? TryRent(string key, Func<TClient, bool> fits)
    {
        Entry? stale = null;
        try
        {
            lock (gate)
            {
                if (!entries.TryGetValue(key, out var node)) return null;
                if (fits(node.Value.Client) && node.Value.TryRent())
                {
                    recency.Remove(node);
                    recency.AddFirst(node);
                    return new Lease(this, node.Value);
                }

                Detach(node);
                stale = node.Value;
                return null;
            }
        }
        finally { stale?.Retire(); }
    }

    /// <summary>
    /// Admits <paramref name="client"/> as <paramref name="key"/>'s, rented to the caller, retiring the client it replaces
    /// and, past capacity, the one used least recently.
    /// </summary>
    public Lease Admit(string key, TClient client)
    {
        var entry = new Entry(key, client);
        entry.TryRent();
        var retired = new List<Entry>();
        lock (gate)
        {
            if (entries.TryGetValue(key, out var replaced)) { Detach(replaced); retired.Add(replaced.Value); }
            entries[key] = recency.AddFirst(entry);
            while (entries.Count > capacity && recency.Last is { } oldest) { Detach(oldest); retired.Add(oldest.Value); }
        }

        foreach (var victim in retired) victim.Retire();
        return new Lease(this, entry);
    }

    /// <summary>Retires <paramref name="key"/>'s client, if the cache holds one.</summary>
    public void Remove(string key)
    {
        Entry? removed = null;
        lock (gate)
            if (entries.TryGetValue(key, out var node)) { Detach(node); removed = node.Value; }
        removed?.Retire();
    }

    // Retires one entry, and forgets it if it is still its key's.
    private void Retire(Entry entry)
    {
        lock (gate)
            if (entries.TryGetValue(entry.Key, out var node) && ReferenceEquals(node.Value, entry)) Detach(node);
        entry.Retire();
    }

    private void Detach(LinkedListNode<Entry> node)
    {
        recency.Remove(node);
        entries.Remove(node.Value.Key);
    }

    /// <summary>One cached client and how many operations hold it.</summary>
    internal sealed class Entry(string key, TClient client)
    {
        private readonly object gate = new();
        private int renters;
        private bool retired;
        private bool disposed;

        public string Key { get; } = key;
        public TClient Client { get; } = client;

        public bool TryRent()
        {
            lock (gate)
            {
                if (retired) return false;
                renters++;
                return true;
            }
        }

        public void Return()
        {
            lock (gate) { renters--; if (!retired || renters > 0 || disposed) return; disposed = true; }
            Client.Dispose();
        }

        public void Retire()
        {
            lock (gate) { retired = true; if (renters > 0 || disposed) return; disposed = true; }
            Client.Dispose();
        }
    }

    /// <summary>A client rented for one operation; disposing the lease returns it.</summary>
    internal sealed class Lease(ClientCache<TClient> cache, Entry entry) : IDisposable
    {
        private int returned;

        public TClient Client => entry.Client;

        /// <summary>Retires the client: the cache forgets it, no later operation rents it, and it is disposed once every
        /// renter returns it.</summary>
        public void Retire() => cache.Retire(entry);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref returned, 1) == 0) entry.Return();
        }
    }
}
