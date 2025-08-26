namespace Sora.Orchestration;

public interface IHostingProvider
{
    string Id { get; }
    int Priority { get; }
    Task<(bool Ok, string? Reason)> IsAvailableAsync(CancellationToken ct = default);

    Task Up(string composePath, Profile profile, RunOptions options, CancellationToken ct = default);
    Task Down(string composePath, StopOptions options, CancellationToken ct = default);

    IAsyncEnumerable<string> Logs(LogsOptions options, CancellationToken ct = default);
    Task<ProviderStatus> Status(StatusOptions options, CancellationToken ct = default);

    // Live port bindings discovered from the provider runtime (e.g., compose ps)
    Task<IReadOnlyList<PortBinding>> LivePorts(CancellationToken ct = default);

    EngineInfo EngineInfo();
}

public sealed record RunOptions(bool Detach, TimeSpan? ReadinessTimeout);
public sealed record StopOptions(bool RemoveVolumes);
public sealed record LogsOptions(string? Service, bool Follow, int? Tail, string? Since = null);
public sealed record StatusOptions(string? Service);
public sealed record ProviderStatus(string Provider, string EngineVersion, IReadOnlyList<(string Service, string State, string? Health)> Services);
public sealed record EngineInfo(string Name, string Version, string Endpoint);

public sealed record PortBinding(string Service, int Host, int Container, string Protocol = "tcp", string? Address = null);
