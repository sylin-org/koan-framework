using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Core;

// Health primitives
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

public interface IHealthService
{
    Task<(HealthState Overall, IReadOnlyList<HealthReport> Reports)> CheckAllAsync(CancellationToken ct = default);
}

internal sealed class HealthService : IHealthService
{
    private readonly IHealthRegistry _registry;
    private readonly IHealthAnnouncementsStore _announcements;
    public HealthService(IHealthRegistry registry, IHealthAnnouncementsStore announcements)
    { _registry = registry; _announcements = announcements; }

    public async Task<(HealthState Overall, IReadOnlyList<HealthReport> Reports)> CheckAllAsync(CancellationToken ct = default)
    {
        var reports = new List<HealthReport>();
        foreach (var c in _registry.All)
        {
            try { reports.Add(await c.CheckAsync(ct)); }
            catch (Exception ex) { reports.Add(new HealthReport(c.Name, c.IsCritical ? HealthState.Unhealthy : HealthState.Degraded, "exception", ex)); }
        }

        // Merge push announcements (treated as non-critical signals unless a matching critical contributor exists)
        var announced = _announcements.Snapshot();
        foreach (var a in announced)
        {
            // If a contributor with same name exists, prefer worst state
            var existing = reports.FirstOrDefault(r => string.Equals(r.Name, a.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                reports.Add(a);
            }
            else
            {
                var worst = (HealthState)Math.Max((int)existing.State, (int)a.State);
                var mergedData = existing.Data ?? a.Data;
                var merged = new HealthReport(existing.Name, worst, existing.Description ?? a.Description, existing.Exception, mergedData);
                reports.Remove(existing);
                reports.Add(merged);
            }
        }

    var overall = reports.Any(r => r.State == HealthState.Unhealthy) && _registry.All.FirstOrDefault(x => x.IsCritical && reports.Any(r => r.Name == x.Name && r.State == HealthState.Unhealthy)) != null
            ? HealthState.Unhealthy
            : reports.Any(r => r.State != HealthState.Healthy) ? HealthState.Degraded : HealthState.Healthy;
        return (overall, reports);
    }
}
