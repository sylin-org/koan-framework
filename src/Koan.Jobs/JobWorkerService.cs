using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Jobs;

/// <summary>
/// Drives the orchestrator in production (Normal mode): boot recovery, then a loop of reaper sweep → scheduled
/// release → drain, paced by <c>PollInterval</c>. Disabled in <see cref="JobMode.Inline"/> and when
/// <c>EnableWorker</c> is false (deterministic tests drive the orchestrator/scheduler directly).
/// </summary>
internal sealed class JobWorkerService : BackgroundService
{
    private readonly JobOrchestrator _orchestrator;
    private readonly JobScheduler _scheduler;
    private readonly JobsOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<JobWorkerService> _logger;

    public JobWorkerService(JobOrchestrator orchestrator, JobScheduler scheduler,
        IOptions<JobsOptions> options, TimeProvider clock, ILogger<JobWorkerService> logger)
    {
        _orchestrator = orchestrator;
        _scheduler = scheduler;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Mode == JobMode.Inline || !_options.EnableWorker) return;

        try
        {
            await _scheduler.RecoverAsync(stoppingToken);          // reclaim anything left mid-flight by a crash
            await _scheduler.SubmitBootActionsAsync(stoppingToken); // fire @boot actions once
        }
        catch (Exception ex) { _logger.LogError(ex, "Job boot sequence failed"); }

        var lastReap = _clock.GetUtcNow();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = _clock.GetUtcNow();
                if (now - lastReap >= _options.ReaperInterval)
                {
                    await _scheduler.ReapAsync(stoppingToken);
                    lastReap = now;
                }
                await _scheduler.TriggerDueAsync(stoppingToken);   // recurring initiator: submit due scheduled actions
                await _orchestrator.DrainAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Job worker iteration failed"); }

            try { await Task.Delay(_options.PollInterval, _clock, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
