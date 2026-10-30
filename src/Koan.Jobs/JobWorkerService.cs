using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Jobs;

/// <summary>
/// Drives the orchestrator in production (Normal mode). After boot recovery, every duty runs on its own timer — the
/// roster heartbeat, the reaper, the scheduler, the archival sweep and the metrics flush — beside a dispatcher that
/// claims ready work up to this node's capacity and starts each execution as a background task this service owns.
/// No duty waits on another, so a node running a long job keeps heartbeating and a node working a backlog keeps
/// scheduling, reaping and archiving. Disabled in <see cref="JobMode.Inline"/> and when <c>EnableWorker</c> is false
/// (deterministic tests drive the orchestrator and scheduler directly).
/// </summary>
internal sealed class JobWorkerService : BackgroundService
{
    private readonly JobOrchestrator _orchestrator;
    private readonly JobScheduler _scheduler;
    private readonly IJobLedger _ledger;
    private readonly JobTypeRegistry _registry;
    private readonly JobWakeCoordinator _wake;
    private readonly JobsOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<JobWorkerService> _logger;
    private readonly ConcurrentDictionary<Task, byte> _executions = new();

    public JobWorkerService(JobOrchestrator orchestrator, JobScheduler scheduler, IJobLedger ledger,
        JobTypeRegistry registry, JobWakeCoordinator wake, IOptions<JobsOptions> options, TimeProvider clock, ILogger<JobWorkerService> logger)
    {
        _orchestrator = orchestrator;
        _scheduler = scheduler;
        _ledger = ledger;
        _registry = registry;
        _wake = wake;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Mode == JobMode.Inline || !_options.EnableWorker) return;

        var scheduled = _registry.All.Sum(b => b.ScheduledActions(_options).Count());
        _logger.LogInformation("[Koan.Jobs] ledger={Ledger} · {Types} job types · {Scheduled} scheduled",
            _ledger.GetType().Name, _registry.Count, scheduled);

        try
        {
            await _orchestrator.BeatAsync(stoppingToken);            // join the fleet roster (PMC-055)
            await _scheduler.RecoverAsync(stoppingToken);          // reclaim anything left mid-flight by a crash
            await _scheduler.SubmitBootActionsAsync(stoppingToken); // fire @boot actions once
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        catch (Exception ex) { _logger.LogError(ex, "Job boot sequence failed"); }

        try
        {
            await Task.WhenAll(
                Duty("roster heartbeat", _options.WorkerHeartbeatInterval, _orchestrator.BeatAsync, runFirst: false, stoppingToken),
                Duty("reaper", _options.ReaperInterval, _scheduler.ReapAsync, runFirst: false, stoppingToken),
                Duty("scheduler", _options.PollInterval, _scheduler.TriggerDueAsync, runFirst: true, stoppingToken),
                Duty("archival sweep", _options.ArchiveInterval, ct => _orchestrator.ArchiveAsync(ct), runFirst: false, stoppingToken),
                _options.MetricsEnabled
                    ? Duty("metrics flush", _options.MetricsFlushInterval, _orchestrator.FlushMetricsAsync, runFirst: false, stoppingToken)
                    : Task.CompletedTask,
                DispatchAsync(stoppingToken));
        }
        finally
        {
            // Every execution observes the stopping token: its handler is cancelled and its job re-queued for reclaim.
            // The service waits for those settlements before it leaves the roster.
            await Task.WhenAll(_executions.Keys);

            // Graceful departure: resign from the fleet roster so peers see the planned shutdown
            // immediately instead of waiting out the death timeout (PMC-055). Best-effort.
            try { await _orchestrator.ResignAsync(CancellationToken.None); }
            catch (Exception ex) { _logger.LogDebug(ex, "Fleet-roster resignation failed on shutdown"); }

            // Flush the last accumulated deltas on graceful stop (best-effort; metrics are lossy-tolerant).
            if (_options.MetricsEnabled)
                try { await _orchestrator.FlushMetricsAsync(CancellationToken.None); } catch (Exception ex) { _logger.LogDebug(ex, "Final metrics flush failed"); }
        }
    }

    /// <summary>One duty on its own timer. The first failure is logged at Error, repeats at Debug, and recovery once at
    /// Information; health is the persistent operator signal. A failing duty never pauses another.</summary>
    private async Task Duty(string name, TimeSpan interval, Func<CancellationToken, Task> run, bool runFirst, CancellationToken stoppingToken)
    {
        var period = Period(interval);
        if (!runFirst && !await Pause(period, stoppingToken)) return;
        var failed = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await run(stoppingToken);
                if (failed)
                {
                    _logger.LogInformation("Job worker {Duty} recovered after a failure", name);
                    failed = false;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                if (!failed) _logger.LogError(ex, "Job worker {Duty} failed; retrying on its interval", name);
                else _logger.LogDebug(ex, "Job worker {Duty} remains unavailable", name);
                failed = true;
            }

            if (!await Pause(period, stoppingToken)) return;
        }
    }

    /// <summary>The dispatcher. It wakes on a submission hint, on a settled execution, on a moved wake stamp
    /// (JOBS-0009) or at <c>PollInterval</c>, claims ready work up to this node's free capacity, starts each execution
    /// in the background and returns to waiting. It never waits for an execution to finish.</summary>
    private async Task DispatchAsync(CancellationToken stoppingToken)
    {
        // With a durable ledger, the wake stamp lets peers notice submissions at WakeProbeInterval cadence while the
        // full claim scan still runs at most one PollInterval apart.
        var probesWakeStamp = _ledger is RoutingJobLedger or DataJobLedger;
        var lastSeenStamp = probesWakeStamp ? await WakeStamp.ReadVersion(stoppingToken) : -1L;
        var lastPass = DateTimeOffset.MinValue;
        var signaled = true;   // the first pass runs at once
        var failed = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pass = !probesWakeStamp || signaled || _clock.GetUtcNow() - lastPass >= _options.PollInterval;
                if (!pass)
                {
                    var stamp = await WakeStamp.ReadVersion(stoppingToken);
                    pass = stamp >= 0 && lastSeenStamp >= 0 && stamp != lastSeenStamp;
                    if (!pass && stamp >= 0) lastSeenStamp = stamp;
                }

                if (pass)
                {
                    await DispatchPassAsync(stoppingToken);
                    lastPass = _clock.GetUtcNow();
                    if (probesWakeStamp)
                    {
                        var absorbed = await WakeStamp.ReadVersion(stoppingToken);   // absorb bumps this pass served
                        if (absorbed >= 0) lastSeenStamp = absorbed;
                    }
                }

                if (failed)
                {
                    _logger.LogInformation("Job worker dispatcher recovered after a failure");
                    failed = false;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                if (!failed) _logger.LogError(ex, "Job worker dispatch failed; retrying at the configured poll interval");
                else _logger.LogDebug(ex, "Job worker dispatch remains unavailable");
                failed = true;
            }

            // A healthy dispatcher wakes at once on a hint or a settled execution, and with a durable ledger re-probes
            // the stamp at WakeProbeInterval. After a failure it bypasses pending hints so an unavailable ledger cannot
            // create a hot retry loop.
            if (failed)
            {
                signaled = false;
                if (!await Pause(Period(_options.PollInterval), stoppingToken)) break;
                continue;
            }

            var wait = probesWakeStamp && _options.WakeProbeInterval < _options.PollInterval
                ? _options.WakeProbeInterval
                : _options.PollInterval;
            try { signaled = await _wake.WaitForWork(Chunk(Period(wait)), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Claim ready work while this node has capacity, starting each execution without awaiting it.</summary>
    private async Task DispatchPassAsync(CancellationToken stoppingToken)
    {
        await _orchestrator.AssignReservationsAsync(stoppingToken);   // dispatch duty when Reservation (PMC-056)
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_options.WorkerConcurrency > 0 && _executions.Count >= _options.WorkerConcurrency) return;
            var execution = await _orchestrator.TryStartNextAsync(stoppingToken, stoppingToken);
            if (execution is null) return;
            Track(execution);
        }
    }

    /// <summary>Own an execution until it settles; its settlement frees capacity and may have appended a successor, so
    /// it hurries the dispatcher.</summary>
    private void Track(Task execution)
    {
        _executions.TryAdd(execution, 0);
        _ = execution.ContinueWith(
            static (settled, state) =>
            {
                var worker = (JobWorkerService)state!;
                worker._executions.TryRemove(settled, out _);
                worker._wake.Nudge();
            },
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>Wait <paramref name="period"/> on the injected clock, in waits no longer than a timer accepts, so any
    /// interval works — <see cref="TimeSpan.MaxValue"/> included, which never elapses. False when the worker stops.</summary>
    private async Task<bool> Pause(TimeSpan period, CancellationToken stoppingToken)
    {
        try
        {
            for (var remaining = period; remaining > TimeSpan.Zero; remaining -= Chunk(remaining))
                await Task.Delay(Chunk(remaining), _clock, stoppingToken);
            return true;
        }
        catch (OperationCanceledException) { return false; }
    }

    /// <summary>The longest single wait a timer accepts is about 49.7 days; a longer interval is waited in parts.</summary>
    private static TimeSpan Chunk(TimeSpan period)
        => period > Infrastructure.Constants.Worker.LongestWait ? Infrastructure.Constants.Worker.LongestWait : period;

    /// <summary>A non-positive interval runs the duty at the poll cadence rather than in a tight loop.</summary>
    private TimeSpan Period(TimeSpan interval)
    {
        var period = interval > TimeSpan.Zero ? interval : _options.PollInterval;
        return period > TimeSpan.Zero ? period : Infrastructure.Constants.Worker.MinimumPeriod;
    }
}
