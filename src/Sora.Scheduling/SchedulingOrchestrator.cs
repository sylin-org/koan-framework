using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Core;

namespace Sora.Scheduling;

internal sealed class SchedulingOrchestrator(
    IOptionsMonitor<SchedulingOptions> options,
    IEnumerable<IScheduledTask> tasks,
    IHealthAggregator health,
    ILogger<SchedulingOrchestrator> logger,
    IHostEnvironment env
) : BackgroundService
{
    private readonly List<Runner> _runners = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var opts = options.CurrentValue;
        if (!opts.Enabled)
        {
            logger.LogInformation("Scheduling disabled (env: {Env})", env.EnvironmentName);
            return;
        }

        // Build runners
        foreach (var t in tasks)
        {
            var job = BuildJob(t, opts);
            if (job is null) continue;
            _runners.Add(job);
        }

        // Start
        var startup = _runners.Where(r => r.OnStartup).ToList();
        foreach (var r in startup)
            _ = r.RunOnceAsync(stoppingToken);

        // Loop for fixed-delay; cron reserved for Phase 2
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var due = _runners.Where(r => r.NextRunUtc is not null && r.NextRunUtc <= now).ToList();
            foreach (var r in due)
                _ = r.RunOnceAsync(stoppingToken);

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private Runner? BuildJob(IScheduledTask task, SchedulingOptions opts)
    {
        var id = task.Id;
        var jobOpts = opts.Jobs.TryGetValue(id, out var j) ? j : null;

        // Merge triggers and policies: config > attribute > interface defaults
        var attr = task.GetType().GetCustomAttributes(typeof(ScheduledAttribute), false).FirstOrDefault() as ScheduledAttribute;

        bool enabled = jobOpts?.Enabled ?? true;
        if (!enabled) return null;

        bool onStartup = jobOpts?.OnStartup ?? attr?.OnStartup ?? task is IOnStartup;

        TimeSpan? fixedDelay = jobOpts?.FixedDelay;
        if (fixedDelay is null && attr?.FixedDelaySeconds is int s) fixedDelay = TimeSpan.FromSeconds(s);
        if (fixedDelay is null && task is IFixedDelay fd) fixedDelay = fd.Delay;

        bool critical = jobOpts?.Critical ?? attr?.Critical ?? (task is IIsCritical);

        TimeSpan? timeout = jobOpts?.Timeout;
        if (timeout is null && attr?.TimeoutSeconds is int ts) timeout = TimeSpan.FromSeconds(ts);
        if (timeout is null && task is IHasTimeout to) timeout = to.Timeout;

        int maxConc = jobOpts?.MaxConcurrency ?? attr?.MaxConcurrency ?? (task is IHasMaxConcurrency mc ? mc.MaxConcurrency : 1);

    return new Runner(task, health, id, onStartup, fixedDelay, critical, timeout, maxConc);
    }

    private sealed class Runner(
        IScheduledTask task,
        IHealthAggregator health,
        string id,
        bool onStartup,
        TimeSpan? fixedDelay,
        bool critical,
        TimeSpan? timeout,
        int maxConcurrency
    )
    {
        // Limit concurrent task executions according to maxConcurrency (default 1)
        private readonly SemaphoreSlim _gate = new(maxConcurrency <= 0 ? 1 : maxConcurrency, maxConcurrency <= 0 ? 1 : maxConcurrency);
        public bool OnStartup { get; } = onStartup;
        public DateTimeOffset? NextRunUtc { get; private set; } = fixedDelay is null ? null : DateTimeOffset.UtcNow + fixedDelay;
        private int _running;
        private int _success;
        private int _fail;
        private string? _lastError;

        public async Task RunOnceAsync(CancellationToken ct)
        {
            if (!await _gate.WaitAsync(0, ct)) return; // max 1 schedule trigger at a time
            try
            {
                Interlocked.Increment(ref _running);
                var cts = timeout is not null ? CancellationTokenSource.CreateLinkedTokenSource(ct) : null;
                if (cts is not null) cts.CancelAfter(timeout!.Value);
                var runCt = cts?.Token ?? ct;

                health.Push($"scheduling:task:{id}", HealthStatus.Healthy, message: "running", ttl: TimeSpan.FromSeconds(30), facts: Facts("running"));
                try
                {
                    await task.RunAsync(runCt);
                    Interlocked.Increment(ref _success);
                    _lastError = null;
                    health.Push($"scheduling:task:{id}", HealthStatus.Healthy, message: "ok", ttl: TimeSpan.FromMinutes(5), facts: Facts("ok"));
                }
                catch (OperationCanceledException) when (runCt.IsCancellationRequested)
                {
                    Interlocked.Increment(ref _fail);
                    _lastError = "timeout";
                    health.Push($"scheduling:task:{id}", HealthStatus.Unhealthy, message: "timeout", ttl: TimeSpan.FromMinutes(5), facts: Facts("timeout"));
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _fail);
                    _lastError = ex.GetType().Name;
                    health.Push($"scheduling:task:{id}", HealthStatus.Unhealthy, message: ex.Message, ttl: TimeSpan.FromMinutes(5), facts: Facts("error"));
                }
                finally
                {
                    if (fixedDelay is not null)
                        NextRunUtc = DateTimeOffset.UtcNow + fixedDelay;
                }
            }
            finally
            {
                _gate.Release();
                Interlocked.Decrement(ref _running);
            }
        }

    private IReadOnlyDictionary<string, string> Facts(string state)
        {
            var dict = new Dictionary<string, string>
            {
                ["id"] = id,
                ["state"] = state,
                ["critical"] = critical ? "true" : "false",
                ["running"] = _running.ToString(),
                ["success"] = _success.ToString(),
                ["fail"] = _fail.ToString(),
            };
            if (_lastError is not null) dict["lastError"] = _lastError;
            return dict;
        }
    }
}
