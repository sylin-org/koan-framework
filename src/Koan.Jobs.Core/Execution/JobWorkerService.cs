using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.BackgroundServices;
using Koan.Jobs.Queue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.Jobs.Execution;

internal sealed class JobWorkerService : KoanBackgroundServiceBase
{
    private readonly IJobQueue _queue;
    private readonly JobExecutor _executor;

    public JobWorkerService(
        IJobQueue queue,
        JobExecutor executor,
        ILogger<JobWorkerService> logger,
        IConfiguration configuration)
        : base(logger, configuration)
    {
        _queue = queue;
        _executor = executor;
    }

    public override string Name => "Koan.Jobs.Worker";
    public override bool IsCritical => true;

    public override Task ExecuteCore(CancellationToken cancellationToken)
        => Run(cancellationToken);

    private async Task Run(CancellationToken cancellationToken)
    {
        // Single serial claimer reads the ready queue and dispatches each item to a tracked task.
        // Concurrency is bounded per lane inside the executor (JobLaneRegistry permit around the
        // job body), so the worker never blocks on dispatch and one slow job can't head-of-line
        // block the rest. On shutdown we await in-flight tasks for a graceful drain (JOBS-0002).
        var inFlight = new ConcurrentDictionary<Task, byte>();
        try
        {
            await foreach (var item in _queue.ReadAll(cancellationToken))
            {
                var task = DispatchAsync(item, cancellationToken);
                inFlight.TryAdd(task, 0);
                _ = task.ContinueWith(
                    t => inFlight.TryRemove(t, out _),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected on shutdown — fall through to drain.
        }
        finally
        {
            var pending = inFlight.Keys.ToArray();
            if (pending.Length > 0)
            {
                try { await Task.WhenAll(pending); }
                catch { /* per-task failures are already logged in DispatchAsync */ }
            }
        }
    }

    private async Task DispatchAsync(JobQueueItem item, CancellationToken cancellationToken)
    {
        try
        {
            await _executor.Execute(item, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected on shutdown.
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Job execution failed for {JobId}", item.JobId);
        }
    }
}
