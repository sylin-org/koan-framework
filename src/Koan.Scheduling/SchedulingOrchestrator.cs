using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.BackgroundServices;
using Koan.Core.Observability.Health;

namespace Koan.Scheduling;

[KoanBackgroundService(RunInProduction = true)]
[ServiceEvent(Koan.Core.Events.KoanServiceEvents.Scheduling.TaskExecuted, EventArgsType = typeof(TaskExecutedEventArgs))]
[ServiceEvent(Koan.Core.Events.KoanServiceEvents.Scheduling.TaskFailed, EventArgsType = typeof(TaskFailedEventArgs))]
[ServiceEvent(Koan.Core.Events.KoanServiceEvents.Scheduling.TaskTimeout, EventArgsType = typeof(TaskTimeoutEventArgs))]
internal sealed class SchedulingOrchestrator : KoanFluentServiceBase
{
    private readonly IOptionsMonitor<SchedulingOptions> _options;
    private readonly IEnumerable<IScheduledTask> _tasks;
    private readonly Koan.Core.Observability.Health.IHealthAggregator _health;
    private readonly IHostEnvironment _env;
    private readonly List<Runner> _runners = new();

    public SchedulingOrchestrator(
        ILogger<SchedulingOrchestrator> logger,
        IConfiguration configuration,
        IOptionsMonitor<SchedulingOptions> options,
        IEnumerable<IScheduledTask> tasks,
        Koan.Core.Observability.Health.IHealthAggregator health,
        IHostEnvironment env)
        : base(logger, configuration)
    {
        _options = options;
        _tasks = tasks;
        _health = health;
        _env = env;
    }

    public override async Task ExecuteCoreAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var opts = _options.CurrentValue;
        if (!opts.Enabled)
        {
            Logger.LogInformation("Scheduling disabled (env: {Env})", _env.EnvironmentName);
            return;
        }

        Logger.LogInformation("Starting scheduling orchestrator - building task runners");

        // Build runners
        foreach (var t in _tasks)
        {
            var job = BuildJob(t, opts);
            if (job is null) continue;
            _runners.Add(job);
        }
        
        Logger.LogInformation("Scheduling orchestrator started with {RunnerCount} task runners", _runners.Count);

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
        
        Logger.LogInformation("Scheduling orchestrator stopped");
    }
    
    [ServiceAction(Koan.Core.Actions.KoanServiceActions.Scheduling.TriggerTask)]
    public Task TriggerTaskAction(string taskId, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Manual task trigger requested for: {TaskId}", taskId);
        
        var runner = _runners.FirstOrDefault(r => r.Id == taskId);
        if (runner != null)
        {
            _ = runner.RunOnceAsync(cancellationToken);
            Logger.LogInformation("Task {TaskId} triggered successfully", taskId);
        }
        else
        {
            Logger.LogWarning("Task {TaskId} not found in runners", taskId);
        }
        return Task.CompletedTask;
    }
    
    [ServiceAction(Koan.Core.Actions.KoanServiceActions.Scheduling.ListTasks)]
    public Task ListTasksAction(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Listing all scheduled tasks:");
        foreach (var runner in _runners)
        {
            Logger.LogInformation("Task: {TaskId}, OnStartup: {OnStartup}, NextRun: {NextRun}", 
                runner.Id, runner.OnStartup, runner.NextRunUtc);
        }
        return Task.CompletedTask;
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

        return new Runner(task, _health, id, onStartup, fixedDelay, critical, timeout, maxConc, this);
    }

    private sealed class Runner
    {
        private readonly IScheduledTask task;
        private readonly Koan.Core.Observability.Health.IHealthAggregator health;
        private readonly TimeSpan? timeout;
        private readonly TimeSpan? fixedDelay;
        private readonly bool critical;
        private readonly SchedulingOrchestrator orchestrator;
        private readonly SemaphoreSlim _gate;
        public string Id { get; }
        public bool OnStartup { get; }
        public DateTimeOffset? NextRunUtc { get; private set; }
        private int _running;
        private int _success;
        private int _fail;
        private string? _lastError;

        public Runner(
            IScheduledTask task,
            Koan.Core.Observability.Health.IHealthAggregator health,
            string id,
            bool onStartup,
            TimeSpan? fixedDelay,
            bool critical,
            TimeSpan? timeout,
            int maxConcurrency,
            SchedulingOrchestrator orchestrator)
        {
            this.task = task;
            this.health = health;
            this.timeout = timeout;
            this.fixedDelay = fixedDelay;
            this.critical = critical;
            this.orchestrator = orchestrator;
            var cap = maxConcurrency <= 0 ? 1 : maxConcurrency;
            _gate = new SemaphoreSlim(cap, cap);
            Id = id;
            OnStartup = onStartup;
            NextRunUtc = fixedDelay is null ? null : DateTimeOffset.UtcNow + fixedDelay;
        }

        public async Task RunOnceAsync(CancellationToken ct)
        {
            if (!await _gate.WaitAsync(0, ct)) return; // max 1 schedule trigger at a time
            try
            {
                Interlocked.Increment(ref _running);
                var cts = timeout is not null ? CancellationTokenSource.CreateLinkedTokenSource(ct) : null;
                if (cts is not null) cts.CancelAfter(timeout!.Value);
                var runCt = cts?.Token ?? ct;

                health.Push($"scheduling:task:{Id}", Koan.Core.Observability.Health.HealthStatus.Healthy, message: "running", ttl: TimeSpan.FromSeconds(30), facts: Facts("running"));
                try
                {
                    await task.RunAsync(runCt);
                    Interlocked.Increment(ref _success);
                    _lastError = null;
                    health.Push($"scheduling:task:{Id}", Koan.Core.Observability.Health.HealthStatus.Healthy, message: "ok", ttl: TimeSpan.FromMinutes(5), facts: Facts("ok"));
                    
                    _ = orchestrator.EmitEventAsync(Koan.Core.Events.KoanServiceEvents.Scheduling.TaskExecuted, new TaskExecutedEventArgs
                    {
                        TaskId = Id,
                        ExecutedAt = DateTimeOffset.UtcNow,
                        Duration = TimeSpan.Zero // Could track actual duration
                    });
                }
                catch (OperationCanceledException) when (runCt.IsCancellationRequested)
                {
                    Interlocked.Increment(ref _fail);
                    _lastError = "timeout";
                    health.Push($"scheduling:task:{Id}", Koan.Core.Observability.Health.HealthStatus.Unhealthy, message: "timeout", ttl: TimeSpan.FromMinutes(5), facts: Facts("timeout"));
                    
                    _ = orchestrator.EmitEventAsync(Koan.Core.Events.KoanServiceEvents.Scheduling.TaskTimeout, new TaskTimeoutEventArgs
                    {
                        TaskId = Id,
                        TimeoutAt = DateTimeOffset.UtcNow,
                        TimeoutDuration = timeout ?? TimeSpan.Zero
                    });
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _fail);
                    _lastError = ex.GetType().Name;
                    health.Push($"scheduling:task:{Id}", Koan.Core.Observability.Health.HealthStatus.Unhealthy, message: ex.Message, ttl: TimeSpan.FromMinutes(5), facts: Facts("error"));
                    
                    _ = orchestrator.EmitEventAsync(Koan.Core.Events.KoanServiceEvents.Scheduling.TaskFailed, new TaskFailedEventArgs
                    {
                        TaskId = Id,
                        Error = ex.Message,
                        FailedAt = DateTimeOffset.UtcNow,
                        Exception = ex
                    });
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
                ["id"] = Id,
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
