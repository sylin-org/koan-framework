using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Routing;

namespace Koan.AI.Integration.Tests.Fixtures;

/// <summary>
/// A simple, thread-safe adapter registry for integration tests.
/// Mirrors the registration semantics of <see cref="InMemoryAdapterRegistry"/>
/// without requiring internal access.
/// </summary>
internal sealed class TestAdapterRegistry : IAiAdapterRegistry
{
    private readonly object _gate = new();
    private readonly List<IAiAdapter> _adapters = [];

    public IReadOnlyList<IAiAdapter> All
    {
        get
        {
            lock (_gate) { return _adapters.ToArray(); }
        }
    }

    public void Add(IAiAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);

        lock (_gate)
        {
            if (_adapters.Any(candidate =>
                    string.Equals(candidate.Id, adapter.Id, StringComparison.OrdinalIgnoreCase)))
                return;

            _adapters.Add(adapter);
        }
    }

    public bool Remove(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        lock (_gate)
        {
            return _adapters.RemoveAll(adapter =>
                string.Equals(adapter.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
        }
    }

    public IAiAdapter? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (_gate)
        {
            return _adapters
                .FirstOrDefault(adapter => string.Equals(adapter.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }
}
