using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Sora.Core;
using Sora.Flow.Runtime;
using Sora.Flow.Options;
using Sora.Flow.Model;
using Sora.Data.Core;

namespace Sora.Flow.Runtime.Dapr;

internal sealed class DaprFlowRuntime : IFlowRuntime
{
    private readonly ILogger<DaprFlowRuntime> _log;
    private readonly IServiceProvider _sp;
    private readonly IOptionsMonitor<FlowOptions> _opts;

    public DaprFlowRuntime(ILogger<DaprFlowRuntime> log, IServiceProvider sp, IOptionsMonitor<FlowOptions> opts)
    { _log = log; _sp = sp; _opts = opts; }

    public Task StartAsync(CancellationToken ct = default)
    {
        _log.LogInformation("Sora.Flow Dapr runtime active");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    { _log.LogInformation("Sora.Flow Dapr runtime stopped."); return Task.CompletedTask; }

    public async Task ReplayAsync(DateTimeOffset? from = null, DateTimeOffset? until = null, CancellationToken ct = default)
    {
        // Minimal behavior: enqueue projection tasks for references marked as RequiresProjection
        // Future: respect time windows using lineage/timestamps when available.
        var viewName = _opts.CurrentValue.DefaultViewName;
        _log.LogInformation("[Dapr] Replay enqueuing tasks for references requiring projection. View={View}", viewName);

        var refs = await ReferenceItem.Query("RequiresProjection == true", ct);
        foreach (var item in refs)
        {
            if (ct.IsCancellationRequested) break;
            await EnqueueIfMissing(item.ReferenceId, item.Version, viewName, ct);
        }
    }

    public async Task ReprojectAsync(string referenceId, string? viewName = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(referenceId)) return;
        var vn = string.IsNullOrWhiteSpace(viewName) ? _opts.CurrentValue.DefaultViewName : viewName!;
        var refItem = await ReferenceItem.Get(referenceId, ct);
        if (refItem is null)
        {
            _log.LogDebug("[Dapr] Reproject: reference not found {ReferenceId}", referenceId);
            return;
        }
        await EnqueueIfMissing(referenceId, refItem.Version, vn, ct);
        _log.LogInformation("[Dapr] Reproject enqueued for {ReferenceId} v{Version} view={View}", referenceId, refItem.Version, vn);
    }

    private static string TaskKey(string referenceId, ulong version, string viewName)
        => $"{referenceId}::{version}::{viewName}";

    private async Task EnqueueIfMissing(string referenceId, ulong version, string viewName, CancellationToken ct)
    {
        // Look for an existing task; if not found, create it.
        var existing = await ProjectionTask.Query($"ReferenceId == '{referenceId}' and Version == {version} and ViewName == '{viewName}'", ct);
        if (existing.Count > 0) return;

        var task = new ProjectionTask
        {
            Id = TaskKey(referenceId, version, viewName),
            ReferenceId = referenceId,
            Version = version,
            ViewName = viewName,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await task.Save(ct);
    }
}
