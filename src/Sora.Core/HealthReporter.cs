namespace Sora.Core;

public static class HealthReporter
{
    // Static convenience one-liners; resolves announcer from AppHost.Current when available
    public static void Healthy(string name) => Resolve()?.Healthy(name);
    public static void Degraded(string name, string? description = null, IReadOnlyDictionary<string, object?>? data = null, TimeSpan? ttl = null)
        => Resolve()?.Degraded(name, description, data, ttl);
    public static void Unhealthy(string name, string? description = null, IReadOnlyDictionary<string, object?>? data = null, TimeSpan? ttl = null)
        => Resolve()?.Unhealthy(name, description, data, ttl);

    private static IHealthAnnouncer? Resolve()
    {
        try { return Sora.Core.Hosting.App.AppHost.Current?.GetService(typeof(IHealthAnnouncer)) as IHealthAnnouncer; }
        catch { return null; }
    }
}