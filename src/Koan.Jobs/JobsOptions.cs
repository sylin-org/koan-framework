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

    /// <summary>Max ready rows the per-lane claim seek pulls per page (ordered, pushed down) before applying the
    /// in-memory pool/gate/exclusive filter. Bounds each lane's head seek to O(batch); a lane pages forward past its
    /// own unclaimable head (gated / pool-exhausted / busy) to its oldest claimable row (JOBS-0008).</summary>
    public int ClaimScanBatch { get; set; } = 64;

    /// <summary>Relative per-lane scheduling weight for the lane-fair claim (JOBS-0008). A lane absent from the map has
    /// weight 1; a lane with weight 2 gets ~twice the dispatch share of a weight-1 lane. Empty (default) = equal-share
    /// round-robin across all lanes — zero-config fairness. Weights are relative, never strict priority (no starvation).</summary>
    public Dictionary<string, double> LaneWeights { get; } = new(StringComparer.Ordinal);

    /// <summary>Self-reporting starvation tripwire (JOBS-0008): a lane whose oldest due-but-unclaimed job has waited
    /// longer than this flips the <c>JobsHealthContributor</c> to <c>Degraded</c>. Zero (default) = off — the per-lane
    /// depth/age facts are always published for scraping, but no Degraded signal unless an operator sets a budget
    /// (opt-in, matching <see cref="JobPerRowWarnThreshold"/> / <see cref="MetricsEnabled"/>).</summary>
    public TimeSpan QueueAgeWarning { get; set; } = TimeSpan.Zero;

    /// <summary>Benign terminal rows (Completed/Cancelled) older than this are purged to keep the active ledger lean.
    /// Zero or negative disables this window.</summary>
    public TimeSpan ArchiveAfter { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Failed/Dead terminal rows older than this are purged (replayable until then). JOBS-0005 §19.3 closes the
    /// hole where they were retained forever — the unbounded half of a high-failure ledger. Zero or negative = retain
    /// indefinitely (the pre-§19 behavior).</summary>
    public TimeSpan FailedAfter { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Hard cap on terminal rows kept per work-type: the newest N survive, older terminal rows are trimmed on
    /// the archival sweep. Bounds a high-throughput burst that age-based windows alone miss (JOBS-0005 §19.3). Zero = no cap.</summary>
    public int RetainPerWorkType { get; set; }

    /// <summary>How often the worker runs the archival sweep.</summary>
    public TimeSpan ArchiveInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Per-work-type active-row count above which the archival sweep logs a job-per-row warning — the §19.4
    /// self-reporting guardrail ("window the source with a conveyor"). The framework can't forbid job-per-row, but it
    /// names it. Zero disables the check.</summary>
    public int JobPerRowWarnThreshold { get; set; } = 100_000;

    /// <summary>Opt-in throughput rollup (JOBS-0005 §20.2). When true, each node accumulates terminal outcomes in
    /// memory and periodically flushes a node-sharded internal rollup. <see cref="JobMetrics"/> provides cheap
    /// throughput/trend summaries that survive retention. Off by default: the zero-config path stays write-free.
    /// (Active counts don't need this — they come from the indexed ledger, §20.1.)</summary>
    public bool MetricsEnabled { get; set; }

    /// <summary>How often the worker flushes the in-memory metrics accumulator to its shard rows (when
    /// <see cref="MetricsEnabled"/>).</summary>
    public TimeSpan MetricsFlushInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Rollup rows last flushed before this window are purged on the archival sweep. Zero or negative =
    /// retain indefinitely.</summary>
    public TimeSpan MetricsRetention { get; set; } = TimeSpan.FromDays(30);
}
