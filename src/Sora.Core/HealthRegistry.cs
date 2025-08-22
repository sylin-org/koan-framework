using System.Collections.Concurrent;

namespace Sora.Core;

internal sealed class HealthRegistry : IHealthRegistry
{
    private readonly ConcurrentDictionary<string, IHealthContributor> _items = new();
    public HealthRegistry(IEnumerable<IHealthContributor> contributors)
    {
        foreach (var c in contributors) _items[c.Name] = c;
    }
    public void Add(IHealthContributor contributor) => _items[contributor.Name] = contributor;
    public IReadOnlyCollection<IHealthContributor> All => _items.Values.ToList();
}