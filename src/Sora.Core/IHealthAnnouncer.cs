namespace Sora.Core;

public interface IHealthAnnouncer
{
    void Healthy(string name);
    void Degraded(string name, string? description = null, IReadOnlyDictionary<string, object?>? data = null, TimeSpan? ttl = null);
    void Unhealthy(string name, string? description = null, IReadOnlyDictionary<string, object?>? data = null, TimeSpan? ttl = null);
}