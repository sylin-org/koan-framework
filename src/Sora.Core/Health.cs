using System.Collections.Concurrent;

namespace Sora.Core;

// Health primitives (legacy contributor contract kept for bridge only)
public enum HealthState { Healthy, Degraded, Unhealthy }

public sealed record HealthReport(string Name, HealthState State, string? Description = null, Exception? Exception = null, IReadOnlyDictionary<string, object?>? Data = null);

public interface IHealthContributor
{
    string Name { get; }
    bool IsCritical { get; }
    Task<HealthReport> CheckAsync(CancellationToken ct = default);
}

internal interface IHealthRegistry
{
    void Add(IHealthContributor contributor);
    IReadOnlyCollection<IHealthContributor> All { get; }
}

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

// Note: Legacy IHealthService removed. Health is computed via IHealthAggregator exclusively.
