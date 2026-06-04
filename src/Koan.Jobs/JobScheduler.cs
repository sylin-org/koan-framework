using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Jobs;

/// <summary>
/// Drives the level-triggered sweeps (JOBS-0005 §6). One engine subsumes the three legacy services: the reaper
/// (<c>@continuous</c>), boot recovery (<c>@boot</c>), and user-declared recurring reconciles. A scheduled action's
/// jobs are parked at submit and this releases (makes due) the ones whose interval has elapsed.
/// </summary>
public sealed class JobScheduler
{
    private readonly IJobLedger _ledger;
    private readonly JobTypeRegistry _registry;
    private readonly JobOrchestrator _orchestrator;
    private readonly JobsOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<JobScheduler> _logger;
    private readonly Dictionary<(string Type, string Action), DateTimeOffset> _lastRun = new();

    public JobScheduler(IJobLedger ledger, JobTypeRegistry registry, JobOrchestrator orchestrator,
        IOptions<JobsOptions> options, TimeProvider clock, ILogger<JobScheduler> logger)
    {
        _ledger = ledger;
        _registry = registry;
        _orchestrator = orchestrator;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Reclaim Running jobs whose lease lapsed (the reaper).</summary>
    public Task ReapAsync(CancellationToken ct = default) => _orchestrator.ReapAsync(ct);

    /// <summary>Boot recovery: reclaim anything left mid-flight by a crash. With the ledger as truth there is no
    /// volatile queue to rebuild — reaping the lapsed-lease set is the whole job.</summary>
    public Task RecoverAsync(CancellationToken ct = default) => ReapAsync(ct);

    /// <summary>Release the parked jobs of every scheduled action whose interval has elapsed (the recurring reconcile).</summary>
    public async Task ReleaseScheduledAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        foreach (var binding in _registry.All)
        {
            foreach (var action in binding.ScheduledActions(_options))
            {
                var (kind, interval) = ParseSchedule(action.Schedule);
                if (kind is ScheduleKind.Boot or ScheduleKind.None) continue;
                if (kind is ScheduleKind.Cron)
                {
                    _logger.LogWarning("Cron schedule '{Schedule}' on {Type}.{Action} is not yet supported (deferred); skipping.",
                        action.Schedule, binding.WorkType, action.Action);
                    continue;
                }

                var key = (binding.WorkType, action.Action);
                if (_lastRun.TryGetValue(key, out var last) && now - last < interval) continue;
                _lastRun[key] = now;

                foreach (var rec in await _ledger.InStage(binding.WorkType, action.Action, ct))
                {
                    if (rec.VisibleAt <= now) continue; // already due
                    rec.VisibleAt = now;
                    rec.Transitions.Add(new JobTransition { At = now, From = rec.Status, To = rec.Status, Note = "released by schedule" });
                    await _ledger.Update(rec, ct);
                }
            }
        }
    }

    internal enum ScheduleKind { None, Interval, Continuous, Boot, Cron }

    internal static (ScheduleKind Kind, TimeSpan Interval) ParseSchedule(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return (ScheduleKind.None, default);
        if (string.Equals(s, "@boot", StringComparison.OrdinalIgnoreCase)) return (ScheduleKind.Boot, default);
        if (string.Equals(s, "@continuous", StringComparison.OrdinalIgnoreCase)) return (ScheduleKind.Continuous, TimeSpan.Zero);
        if (TimeSpan.TryParse(s, out var ts)) return (ScheduleKind.Interval, ts);
        return (ScheduleKind.Cron, default);
    }
}
