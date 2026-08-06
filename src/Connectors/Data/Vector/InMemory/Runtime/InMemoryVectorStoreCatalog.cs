namespace Koan.Data.Vector.Connector.InMemory;

internal sealed class InMemoryVectorStoreCatalog : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, object> _stores = new(StringComparer.Ordinal);
    private readonly int _capacity;
    private bool _disposed;

    public InMemoryVectorStoreCatalog(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    public InMemoryVectorStore<TKey> GetOrAdd<TKey>(string route, int maxPoints)
        where TKey : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stores.TryGetValue(route, out var existing))
                return existing as InMemoryVectorStore<TKey>
                    ?? throw new InvalidOperationException($"Vector route '{route}' was compiled with a different key type.");
            if (_stores.Count >= _capacity)
                throw new InvalidOperationException(
                    $"InMemory Vector reached its configured limit of {_capacity} physical spaces. " +
                    "Reduce source/partition spaces or increase Koan:Data:Vector:InMemory:MaxSpaces.");
            var created = new InMemoryVectorStore<TKey>(maxPoints);
            _stores.Add(route, created);
            return created;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _stores.Clear();
        }
    }
}
