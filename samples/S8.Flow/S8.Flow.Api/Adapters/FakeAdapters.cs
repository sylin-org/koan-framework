namespace S8.Flow.Api.Adapters;

public sealed class AdapterHealth
{
    public string Name { get; init; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastEmitAt { get; init; }
    public int Emitted { get; init; }
    public string Status { get; init; } = "unknown";
}

public interface IAdapterHealthRegistry
{
    IReadOnlyDictionary<string, AdapterHealth> Snapshot();
}

internal sealed class NullAdapterHealthRegistry : IAdapterHealthRegistry
{
    public IReadOnlyDictionary<string, AdapterHealth> Snapshot() => new Dictionary<string, AdapterHealth>(StringComparer.OrdinalIgnoreCase);
}
