namespace Koan.Jobs;

/// <summary>How the orchestrator executes submitted jobs.</summary>
public enum JobMode
{
    /// <summary>A background worker claims and runs jobs asynchronously (production).</summary>
    Normal = 0,
    /// <summary>Submit executes synchronously on the caller — deterministic, zero-infrastructure tests.</summary>
    Inline = 1,
}

/// <summary>Knobs for the Jobs pillar. Per-action policy lives on <c>[JobAction]</c>; these are the
/// type-/host-level defaults and engine settings (JOBS-0005).</summary>
public sealed class JobsOptions
{
    public JobMode Mode { get; set; } = JobMode.Normal;

    /// <summary>How long a claim's lease is held before the reaper may reclaim it.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Worker poll cadence when idle (a wake signal short-circuits this).</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Reaper sweep cadence (Running &amp;&amp; lease lapsed → reclaim).</summary>
    public TimeSpan ReaperInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Default retry attempts when an action doesn't specify <c>MaxAttempts</c>.</summary>
    public int DefaultMaxAttempts { get; set; } = 3;

    /// <summary>Default per-lane concurrency when an action doesn't specify <c>MaxConcurrency</c>.</summary>
    public int DefaultMaxConcurrency { get; set; } = 8;

    /// <summary>Default total wall-clock deferral guard (ratified: 24h) — a forgotten Deadline can't defer forever.</summary>
    public TimeSpan DefaultDeadline { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Default reschedule-count guard; &lt;0 = off (secondary spin-guard).</summary>
    public int DefaultMaxReschedules { get; set; } = -1;

    /// <summary>Spread applied past a deferral's release time to avoid a thundering herd.</summary>
    public TimeSpan RescheduleJitter { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Base delay for exponential retry backoff.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Max concurrent in-flight executions for this worker/node.</summary>
    public int WorkerConcurrency { get; set; } = Math.Max(2, Environment.ProcessorCount);

    /// <summary>When false, the background worker loop does not run — the orchestrator is driven explicitly
    /// (deterministic tests advance the clock and call Drain/Reap/ReleaseScheduled themselves).</summary>
    public bool EnableWorker { get; set; } = true;

    /// <summary>How the durable tier secures a claim under competing consumers (default <see cref="ClaimStrategy.Optimistic"/>).</summary>
    public ClaimStrategy ClaimStrategy { get; set; } = ClaimStrategy.Optimistic;

    /// <summary>The reservation window for <see cref="ClaimStrategy.Ticket"/> — must exceed clock skew + write propagation.</summary>
    public TimeSpan ClaimWindow { get; set; } = TimeSpan.FromSeconds(1);
}
