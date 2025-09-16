using Koan.AI.Contracts.Routing;

namespace Koan.AI;

internal sealed class InMemoryAdapterRegistry : IAiAdapterRegistry
{
    private readonly object _gate = new();
    private readonly List<Contracts.Adapters.IAiAdapter> _adapters = new();
    public IReadOnlyList<Contracts.Adapters.IAiAdapter> All
    { get { lock (_gate) return _adapters.ToArray(); } }
    public void Add(Contracts.Adapters.IAiAdapter adapter)
    { lock (_gate) { if (!_adapters.Any(a => a.Id == adapter.Id)) _adapters.Add(adapter); } }
    public bool Remove(string id)
    { lock (_gate) { return _adapters.RemoveAll(a => a.Id == id) > 0; } }
    public Contracts.Adapters.IAiAdapter? Get(string id)
    { lock (_gate) { return _adapters.FirstOrDefault(a => a.Id == id); } }
}