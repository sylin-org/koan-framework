using Microsoft.Extensions.Logging;

namespace Koan.Flow.Runtime;

internal sealed class InMemoryFlowRuntime : IFlowRuntime
{
    private readonly ILogger<InMemoryFlowRuntime> _log;
    public InMemoryFlowRuntime(ILogger<InMemoryFlowRuntime> log) => _log = log;

    public Task StartAsync(CancellationToken ct = default)
    { _log.LogInformation("Koan.Flow InMemory runtime started."); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct = default)
    { _log.LogInformation("Koan.Flow InMemory runtime stopped."); return Task.CompletedTask; }
    public Task ReplayAsync(DateTimeOffset? from = null, DateTimeOffset? until = null, CancellationToken ct = default)
    { _log.LogInformation("Replay requested: {from}..{until}", from, until); return Task.CompletedTask; }
    public Task ReprojectAsync(string referenceId, string? viewName = null, CancellationToken ct = default)
    { _log.LogInformation("Reproject requested: {ref} view={view}", referenceId, viewName); return Task.CompletedTask; }
}
