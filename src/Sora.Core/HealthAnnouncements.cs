using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Core;

public interface IHealthAnnouncer
{
    void Healthy(string name);
    void Degraded(string name, string? description = null, IReadOnlyDictionary<string, object?>? data = null, TimeSpan? ttl = null);
    void Unhealthy(string name, string? description = null, IReadOnlyDictionary<string, object?>? data = null, TimeSpan? ttl = null);
}

internal interface IHealthAnnouncementsStore
{
    IReadOnlyList<HealthReport> Snapshot();
}

internal sealed class HealthAnnouncements : IHealthAnnouncer, IHealthAnnouncementsStore
{
    private sealed record Entry(HealthState State, string? Description, IReadOnlyDictionary<string, object?>? Data, DateTimeOffset ExpiresAt, DateTimeOffset? LastNonHealthyAt);
    private readonly ConcurrentDictionary<string, Entry> _items = new();
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(2);

    public void Healthy(string name)
    {
        // Healthy clears the message immediately
        _items.TryRemove(name, out _);
    }

    public void Degraded(string name, string? description = null, IReadOnlyDictionary<string, object?>? data = null, TimeSpan? ttl = null)
        => Set(name, HealthState.Degraded, description, data, ttl);

    public void Unhealthy(string name, string? description = null, IReadOnlyDictionary<string, object?>? data = null, TimeSpan? ttl = null)
        => Set(name, HealthState.Unhealthy, description, data, ttl);

    private void Set(string name, HealthState state, string? description, IReadOnlyDictionary<string, object?>? data, TimeSpan? ttl)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now + (ttl ?? DefaultTtl);
        _items.AddOrUpdate(name,
            _ => new Entry(state, description, data, expires, now),
            (_, prev) => new Entry(state, description, data, expires, prev.LastNonHealthyAt ?? now));
    }

    public IReadOnlyList<HealthReport> Snapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var results = new List<HealthReport>();
        foreach (var kvp in _items.ToArray())
        {
            var name = kvp.Key;
            var e = kvp.Value;
            if (e.ExpiresAt <= now)
            {
                _items.TryRemove(name, out _);
                continue;
            }
            var data = e.Data ?? (e.LastNonHealthyAt is not null
                ? new Dictionary<string, object?> { ["lastNonHealthyAt"] = e.LastNonHealthyAt.Value.ToString("O") }
                : null);
            results.Add(new HealthReport(name, e.State, e.Description, null, data));
        }
        return results;
    }
}

public static class HealthReporter
{
    // Static convenience one-liners; resolves announcer from SoraApp.Current when available
    public static void Healthy(string name) => Resolve()?.Healthy(name);
    public static void Degraded(string name, string? description = null, IReadOnlyDictionary<string, object?>? data = null, TimeSpan? ttl = null)
        => Resolve()?.Degraded(name, description, data, ttl);
    public static void Unhealthy(string name, string? description = null, IReadOnlyDictionary<string, object?>? data = null, TimeSpan? ttl = null)
        => Resolve()?.Unhealthy(name, description, data, ttl);

    private static IHealthAnnouncer? Resolve()
    {
        try { return SoraApp.Current?.GetService(typeof(IHealthAnnouncer)) as IHealthAnnouncer; }
        catch { return null; }
    }
}
