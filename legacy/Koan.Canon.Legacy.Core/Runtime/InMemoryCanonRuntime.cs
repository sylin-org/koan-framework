using Microsoft.Extensions.Logging;

namespace Koan.Canon.Runtime;

internal sealed class InMemoryCanonRuntime : ICanonRuntime
{
    private readonly ILogger<InMemoryCanonRuntime> _log;
    public InMemoryCanonRuntime(ILogger<InMemoryCanonRuntime> log) => _log = log;

    public Task StartAsync(CancellationToken ct = default)
    { _log.LogInformation("Koan.Canon InMemory runtime started."); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct = default)
    { _log.LogInformation("Koan.Canon InMemory runtime stopped."); return Task.CompletedTask; }
    public Task ReplayAsync(DateTimeOffset? from = null, DateTimeOffset? until = null, CancellationToken ct = default)
    { _log.LogInformation("Replay requested: {from}..{until}", from, until); return Task.CompletedTask; }
    public Task ReprojectAsync(string referenceId, string? viewName = null, CancellationToken ct = default)
    { _log.LogInformation("Reproject requested: {ref} view={view}", referenceId, viewName); return Task.CompletedTask; }
}


