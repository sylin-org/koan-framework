using Koan.AI.Contracts.Adapters;

namespace Koan.AI.Contracts.Routing;

public interface IAiAdapterRegistry
{
    IReadOnlyList<IAiAdapter> All { get; }
    IAiAdapter? Get(string id);
}
