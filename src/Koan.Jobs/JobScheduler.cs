using Cronos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Jobs;

/// <summary>
/// The recurring initiator (JOBS-0005 §6). Scheduling is separated from jobs: a scheduled action does not park —
/// the scheduler <b>submits a fresh job on its cadence</b>, going through the normal claim/run/settle path.
/// Recurrence comes from re-submitting; overlap is collapsed by coalescing (a stable <c>[JobIdempotent]</c> key).
/// One engine subsumes the legacy reaper (<c>@continuous</c> reclaim), boot recovery (<c>@boot</c>), and recurring
/// reconciles. Every submit is a type-level <see cref="IJobCoordinator.TriggerAsync"/> against the per-type singleton.
/// </summary>
internal sealed class JobScheduler
{
    private readonly IJobCoordinator _coordinator;
    private readonly JobTypeRegistry _registry;
    private readonly JobOrchestrator _orchestrator;
    private readonly JobsOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<JobScheduler> _logger;
    private readonly Dictionary<(string Type, string Action), DateTimeOffset> _lastRun = new();
    private readonly Dictionary<string, CronExpression?> _cronCache = new(StringComparer.Ordinal);

    public JobScheduler(IJobCoordinator coordinator, JobTypeRegistry registry, JobOrchestrator orchestrator,
        IOptions<JobsOptions> options, TimeProvider clock, ILogger<JobScheduler> logger)
    {
        _coordinator = coordinator;
        _registry = registry;
        _orchestrator = orchestrator;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Reclaim Running jobs whose lease lapsed (the reaper).</summary>
    public Task ReapAsync(CancellationToken ct = default) => _orchestrator.ReapAsync(ct);

    /// <summary>Boot recovery: reclaim anything left mid-flight by a crash. The ledger is the truth, so reaping the
    /// lapsed-lease set is the whole job.</summary>
    public Task RecoverAsync(CancellationToken ct = default) => ReapAsync(ct);

    /// <summary>Submit each <c>@boot</c> action once (called at startup).</summary>
    public async Task SubmitBootActionsAsync(CancellationToken ct = default)
    {
        foreach (var binding in _registry.All)
            foreach (var action in binding.ScheduledActions(_options))
                if (ParseSchedule(action.Schedule).Kind == ScheduleKind.Boot)
                    await _coordinator.TriggerAsync(binding.WorkType, action.Action, ct);
    }

    /// <summary>Submit a fresh job for every scheduled action whose cadence is due. The worker calls this each tick;
    /// tests call it explicitly. First fire is immediate; thereafter gated by the interval. <c>@continuous</c> fires
    /// every tick.</summary>
    public async Task TriggerDueAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        foreach (var binding in _registry.All)
        {
            foreach (var action in binding.ScheduledActions(_options))
            {
                var (kind, interval) = ParseSchedule(action.Schedule);
                if (kind is ScheduleKind.Boot or ScheduleKind.None) continue;

                var key = (binding.WorkType, action.Action);

                if (kind is ScheduleKind.Cron)
                {
                    var cron = GetCron(action.Schedule!);
                    if (cron is null)
                    {
                        _logger.LogWarning("Invalid cron schedule '{Schedule}' on {Type}.{Action}; skipping.",
                            action.Schedule, binding.WorkType, action.Action);
                        continue;
                    }
                    // Baseline on first sight (cron fires at its scheduled time, not at startup), then fire once the
                    // next occurrence after the last fire has passed (missed occurrences are not replayed).
                    if (!_lastRun.TryGetValue(key, out var lastCron)) { _lastRun[key] = now; continue; }
                    var next = cron.GetNextOccurrence(lastCron.UtcDateTime);
                    if (next is not { } occurrence || now.UtcDateTime < occurrence) continue;
                    _lastRun[key] = now;
                    await _coordinator.TriggerAsync(binding.WorkType, action.Action, ct);
                    continue;
                }

                // interval / @continuous
                if (_lastRun.TryGetValue(key, out var last) && now - last < interval) continue;
                _lastRun[key] = now;
                await _coordinator.TriggerAsync(binding.WorkType, action.Action, ct);
            }
        }
    }

    private CronExpression? GetCron(string schedule)
    {
        if (_cronCache.TryGetValue(schedule, out var cached)) return cached;
        CronExpression? parsed;
        try { parsed = CronExpression.Parse(StripCronWrapper(schedule)); }
        catch (CronFormatException) { parsed = null; }
        _cronCache[schedule] = parsed;
        return parsed;
    }

    /// <summary>Accept both a bare cron expression (<c>"0 2 * * *"</c>) and a <c>cron(...)</c> wrapper.</summary>
    private static string StripCronWrapper(string s)
    {
        s = s.Trim();
        return s.StartsWith("cron(", StringComparison.OrdinalIgnoreCase) && s.EndsWith(")")
            ? s[5..^1].Trim()
            : s;
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
