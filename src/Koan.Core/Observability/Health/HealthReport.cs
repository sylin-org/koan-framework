namespace Koan.Core.Observability.Health;

public sealed record HealthReport(
    string Name,
    HealthState State,
    string? Description,
    TimeSpan? Ttl,
    IReadOnlyDictionary<string, object?>? Data
)
{
    public static HealthReport Healthy(string description) =>
        new HealthReport("background-services", HealthState.Healthy, description, null, null);

    public static HealthReport Unhealthy(string description) =>
        new HealthReport("background-services", HealthState.Unhealthy, description, null, null);
}