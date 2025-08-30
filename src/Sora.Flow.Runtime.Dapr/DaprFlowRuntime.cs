using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Flow.Runtime;

namespace Sora.Flow.Runtime.Dapr;

internal sealed class DaprFlowRuntime : IFlowRuntime
{
    private readonly ILogger<DaprFlowRuntime> _log;
    public DaprFlowRuntime(ILogger<DaprFlowRuntime> log) => _log = log;
    public Task StartAsync(CancellationToken ct = default)
    { _log.LogInformation("Sora.Flow Dapr runtime active (placeholder)."); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct = default)
    { _log.LogInformation("Sora.Flow Dapr runtime stopped."); return Task.CompletedTask; }
    public Task ReplayAsync(DateTimeOffset? from = null, DateTimeOffset? until = null, CancellationToken ct = default)
    { _log.LogInformation("[Dapr] Replay requested from {From} to {Until}", from, until); return Task.CompletedTask; }
    public Task ReprojectAsync(string referenceId, string? viewName = null, CancellationToken ct = default)
    { _log.LogInformation("[Dapr] Reproject requested for {ReferenceId} view {View}", referenceId, viewName ?? "(all)"); return Task.CompletedTask; }
}
