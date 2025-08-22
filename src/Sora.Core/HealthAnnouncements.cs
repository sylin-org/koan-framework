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

// Adapter over the new IHealthAggregator for backward compatibility with HealthReporter
internal sealed class HealthAnnouncements : IHealthAnnouncer, IHealthAnnouncementsStore
{
    private readonly IHealthAggregator _agg;
    private readonly HealthAggregatorOptions _opt;
    public HealthAnnouncements(IHealthAggregator agg, HealthAggregatorOptions opt)
    { _agg = agg; _opt = opt; }

    public void Healthy(string name)
        => _agg.Push(name, HealthStatus.Healthy, message: null, ttl: null, facts: null);

    public void Degraded(string name, string? description = null, IReadOnlyDictionary<string, object?>? data = null, TimeSpan? ttl = null)
        => _agg.Push(name, HealthStatus.Degraded, description, ttl, ToFacts(data));

    public void Unhealthy(string name, string? description = null, IReadOnlyDictionary<string, object?>? data = null, TimeSpan? ttl = null)
        => _agg.Push(name, HealthStatus.Unhealthy, description, ttl, ToFacts(data));

    public IReadOnlyList<HealthReport> Snapshot()
    {
        var snap = _agg.GetSnapshot();
        return snap.Components
            .Select(s => new HealthReport(
                s.Component,
                Map(s.Status),
                s.Message,
                null,
                s.Facts?.ToDictionary(k => (string)k.Key, v => (object?)v.Value)
            ))
            .ToList();
    }

    private static IReadOnlyDictionary<string, string>? ToFacts(IReadOnlyDictionary<string, object?>? data)
    {
        if (data is null) return null;
        var dict = new Dictionary<string, string>();
        foreach (var kv in data)
        {
            if (kv.Value is null) continue;
            dict[kv.Key] = kv.Value.ToString() ?? string.Empty;
        }
        return dict;
    }

    private static HealthState Map(HealthStatus s) => s switch
    {
        HealthStatus.Healthy => HealthState.Healthy,
        HealthStatus.Degraded => HealthState.Degraded,
        HealthStatus.Unhealthy => HealthState.Unhealthy,
        _ => HealthState.Degraded,
    };
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
