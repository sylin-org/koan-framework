using Sora.AI.Contracts.Adapters;

namespace Sora.AI.Contracts.Routing;

public interface IAiAdapterRegistry
{
    IReadOnlyList<IAiAdapter> All { get; }
    void Add(IAiAdapter adapter);
    bool Remove(string id);
    IAiAdapter? Get(string id);
}
