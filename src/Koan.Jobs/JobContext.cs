using Microsoft.Extensions.Logging;

namespace Koan.Jobs;

/// <summary>
/// The handler's whole world for one execution (JOBS-0005 §4.2). Carries the current <see cref="Action"/>, DI
/// (<see cref="Services"/>), logging, cancellation, the read-only orchestration <see cref="State"/>, durable
/// <see cref="Progress"/>, and the control verbs (chain branch/stop, reschedule, cooperative backoff). The
/// handler signals its non-throw outcome by calling at most one control verb; the orchestrator reads it
/// after <c>Execute</c> returns.
/// </summary>
public sealed class JobContext
{
    private readonly TimeProvider _clock;
    private readonly Func<string, double, string?, CancellationToken, Task>? _progressSink;

    internal JobContext(
        string action,
        string jobId,
        IServiceProvider services,
        ILogger logger,
        JobState state,
        TimeProvider clock,
        CancellationToken cancellation,
        Func<string, double, string?, CancellationToken, Task>? progressSink = null)
    {
        Action = action;
        JobId = jobId;
        Services = services;
        Logger = logger;
        State = state;
        Cancellation = cancellation;
        _clock = clock;
        _progressSink = progressSink;
    }

    /// <summary>The stage being executed (empty for a single-action job).</summary>
    public string Action { get; }

    /// <summary>The ledger entry id (the Job id), distinct from the work-item id.</summary>
    public string JobId { get; }

    /// <summary>DI for handler bodies (the common case; an <c>IKoanJobHandler&lt;T&gt;</c> class is the escape hatch).</summary>
    public IServiceProvider Services { get; }

    public ILogger Logger { get; }

    /// <summary>Cooperative cancellation (durable cancel marker or per-action timeout fires this).</summary>
    public CancellationToken Cancellation { get; }

    /// <summary>Read-only orchestration snapshot for stateful decisions.</summary>
    public JobState State { get; }

    // --- outcome the handler signalled (read by the orchestrator post-execute; public for test assertions) ---
    public JobSignal Signal { get; private set; } = JobSignal.None;
    public DateTimeOffset? DeferUntil { get; private set; }
    public bool GateKeyOverrideSet { get; private set; }
    public string? GateKeyOverride { get; private set; }
    public string? NextAction { get; private set; }

    /// <summary>Report durable progress (persisted to the ledger; surfaced to dashboards).</summary>
    public Task Progress(double fraction, string? message = null)
        => _progressSink?.Invoke(JobId, fraction, message, Cancellation) ?? Task.CompletedTask;

    /// <summary>Branch: override the next stage (replaces the declared <c>[JobChain]</c> default).</summary>
    public void ContinueWith(string action)
    {
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("Action must be non-empty.", nameof(action));
        SetSignal(JobSignal.ContinueWith);
        NextAction = action;
    }

    /// <summary>Terminal after this step, despite a declared <c>[JobChain]</c>.</summary>
    public void StopChain() => SetSignal(JobSignal.StopChain);

    /// <summary>Defer THIS job (same stage) by a relative delay. Does NOT consume a retry attempt.</summary>
    public void Reschedule(TimeSpan after)
    {
        SetSignal(JobSignal.Reschedule);
        DeferUntil = _clock.GetUtcNow() + (after < TimeSpan.Zero ? TimeSpan.Zero : after);
    }

    /// <summary>Defer THIS job to an absolute time (e.g. "tomorrow 09:00"). Does NOT consume a retry attempt.</summary>
    public void Reschedule(DateTimeOffset until)
    {
        SetSignal(JobSignal.Reschedule);
        DeferUntil = until;
    }

    /// <summary>Set a shared resource gate (default = this job's gate key) until <c>now+after</c>, and reschedule
    /// this job. Peer jobs for the same key defer at dispatch without running. Does NOT consume a retry attempt.</summary>
    public void Backoff(TimeSpan after, string? key = null)
    {
        SetSignal(JobSignal.Backoff);
        DeferUntil = _clock.GetUtcNow() + (after < TimeSpan.Zero ? TimeSpan.Zero : after);
        GateKeyOverrideSet = true;
        GateKeyOverride = key;
    }

    private void SetSignal(JobSignal signal)
    {
        if (Signal != JobSignal.None)
            throw new InvalidOperationException(
                $"A job handler raised conflicting control signals ({Signal} then {signal}); call at most one.");
        Signal = signal;
    }
}
