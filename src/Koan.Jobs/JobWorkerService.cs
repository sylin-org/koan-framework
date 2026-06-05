using System.Linq;
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
    private readonly IJobLedger _ledger;
    private readonly JobTypeRegistry _registry;
    private readonly IJobTransport _transport;
    private readonly JobsOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<JobWorkerService> _logger;

    public JobWorkerService(JobOrchestrator orchestrator, JobScheduler scheduler, IJobLedger ledger,
        JobTypeRegistry registry, IJobTransport transport, IOptions<JobsOptions> options, TimeProvider clock, ILogger<JobWorkerService> logger)
    {
        _orchestrator = orchestrator;
        _scheduler = scheduler;
        _ledger = ledger;
        _registry = registry;
        _transport = transport;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Mode == JobMode.Inline || !_options.EnableWorker) return;

        var scheduled = _registry.All.Sum(b => b.ScheduledActions(_options).Count());
        _logger.LogInformation("[Koan.Jobs] ledger={Ledger} · {Types} job types · {Scheduled} scheduled · claim={Claim}",
            _ledger.GetType().Name, _registry.Count, scheduled, _options.ClaimStrategy);

        try
        {
            await _scheduler.RecoverAsync(stoppingToken);          // reclaim anything left mid-flight by a crash
            await _scheduler.SubmitBootActionsAsync(stoppingToken); // fire @boot actions once
        }
        catch (Exception ex) { _logger.LogError(ex, "Job boot sequence failed"); }

        var lastReap = _clock.GetUtcNow();
        var lastArchive = _clock.GetUtcNow();
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
                if (now - lastArchive >= _options.ArchiveInterval)
                {
                    await _orchestrator.ArchiveAsync(stoppingToken);
                    lastArchive = now;
                }
                await _scheduler.TriggerDueAsync(stoppingToken);   // recurring initiator: submit due scheduled actions
                await _orchestrator.DrainAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Job worker iteration failed"); }

            // Push-dispatch: wake immediately on a submit signal, else fall back to the poll interval.
            try { await _transport.WaitForWork(_options.PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
