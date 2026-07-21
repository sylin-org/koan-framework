using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Routing;

namespace Koan.AI.EndToEnd.Tests;

internal sealed class TestAdapterRegistry : IAiAdapterRegistry
{
    private readonly object _gate = new();
    private readonly List<IAiAdapter> _adapters = [];

    public IReadOnlyList<IAiAdapter> All
    {
        get
        {
            lock (_gate)
            {
                return _adapters.ToArray();
            }
        }
    }

    public void Add(IAiAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);

        lock (_gate)
        {
            if (_adapters.Any(candidate =>
                    string.Equals(candidate.Id, adapter.Id, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            _adapters.Add(adapter);
        }
    }

    public IAiAdapter? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        lock (_gate)
        {
            return _adapters.FirstOrDefault(adapter =>
                string.Equals(adapter.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }
}
