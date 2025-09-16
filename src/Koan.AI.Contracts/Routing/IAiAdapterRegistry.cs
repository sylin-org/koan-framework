using Koan.AI.Contracts.Adapters;

namespace Koan.AI.Contracts.Routing;

public interface IAiAdapterRegistry
{
    IReadOnlyList<IAiAdapter> All { get; }
    void Add(IAiAdapter adapter);
    bool Remove(string id);
    IAiAdapter? Get(string id);
}
