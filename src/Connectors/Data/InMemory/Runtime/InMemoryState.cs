using System.Collections.Concurrent;
namespace Koan.Data.Connector.InMemory.Runtime;

/// <summary>Finite host-owned storage for detached Entity snapshots.</summary>
internal sealed class InMemoryState
{
    private readonly ConcurrentDictionary<StoreKey, object> _stores = new();
    private readonly object _creationGate = new();

    internal ConcurrentDictionary<TKey, Record> Store<TKey>(string source, Type root, string partition)
        where TKey : notnull
    {
        var key = new StoreKey(source, root, partition);
        if (_stores.TryGetValue(key, out var current))
            return Cast<TKey>(current, key);

        lock (_creationGate)
        {
            if (_stores.TryGetValue(key, out current)) return Cast<TKey>(current, key);
            if (_stores.Count >= Infrastructure.Constants.Provider.MaximumStoresPerHost)
                throw new InvalidOperationException(
                    $"InMemory reached the host bound of {Infrastructure.Constants.Provider.MaximumStoresPerHost} " +
                    "source/root/partition stores.");
            var created = new ConcurrentDictionary<TKey, Record>();
            if (!_stores.TryAdd(key, created))
                throw new InvalidOperationException("InMemory could not publish a newly reserved host store.");
            return created;
        }
    }

    private readonly ConcurrentDictionary<string, string> _markers = new(StringComparer.Ordinal);

    /// <summary>
    /// The store marker of a source (DATA-0148), keyed exactly as <see cref="Store{TKey}"/> keys its stores. It lives as
    /// long as the host's stores do and is created only when <paramref name="create"/> is set.
    /// </summary>
    internal string? Marker(string source, bool create) =>
        create
            ? _markers.GetOrAdd(source, static _ => Koan.Data.Abstractions.StoreMarker.NewId())
            : _markers.TryGetValue(source, out var marker) ? marker : null;

    internal readonly record struct Record(
        string EntityJson,
        IReadOnlyDictionary<string, object?>? Managed);

    private static ConcurrentDictionary<TKey, Record> Cast<TKey>(object value, StoreKey key)
        where TKey : notnull => value as ConcurrentDictionary<TKey, Record>
        ?? throw new InvalidOperationException(
            $"InMemory root '{key.Root.FullName}' was requested with conflicting key types.");

    private readonly record struct StoreKey(string Source, Type Root, string Partition);
}
