namespace Koan.Orchestration.Abstractions;

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