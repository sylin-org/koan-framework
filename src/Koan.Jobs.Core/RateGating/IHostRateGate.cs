namespace Koan.Jobs.RateGating;

/// <summary>
/// Cross-job rate-limit gate, keyed by host tag (e.g. <c>"nexusmods"</c>, <c>"ko-fi"</c>). Lets a
/// single rate-limit failure pause every other job targeting the same upstream until the gate
/// clears — without needing per-job retry coordination at the call site.
/// </summary>
/// <remarks>
/// <para>
/// <b>Convention.</b> A job declares its target host by setting
/// <c>job.Metadata["host"] = "{hostTag}"</c>. The <see cref="Execution.JobExecutor"/> reads this at
/// dispatch time and consults <see cref="TryGetGate"/>; if a gate is active for the host, the job is
/// re-queued with <c>QueuedAt = ReleaseAt</c> instead of running. No exception path; no retry budget
/// consumed.
/// </para>
/// <para>
/// <b>Triggering a gate.</b> A job handler that detects an upstream rate-limit (HTTP 429 with
/// <c>Retry-After</c>, provider-specific quota response, etc.) throws
/// <see cref="RateLimitedJobException"/> carrying the host tag and retry-after duration. The
/// executor catches it, sets the gate automatically, and applies <c>RetryAfter</c> as the
/// individual retry delay. Subsequent dispatches of any job tagged with the same host short-circuit
/// to "wait for gate."
/// </para>
/// <para>
/// <b>Implementations.</b> <see cref="InMemoryHostRateGate"/> is the default — process-local, fast,
/// loses state on restart. Consumers needing cross-process gating can register a custom
/// implementation that persists to their data backend.
/// </para>
/// </remarks>
public interface IHostRateGate
{
    /// <summary>
    /// True when the host is currently gated. Stale entries (release time in the past) are treated
    /// as no gate.
    /// </summary>
    bool TryGetGate(string hostTag, out RateGateEntry gate);

    /// <summary>
    /// Set or extend a gate for the named host. If a gate already exists with a later release time
    /// than the requested one, the existing gate is kept (latest release wins).
    /// </summary>
    Task GateHost(string hostTag, TimeSpan duration, string reason, CancellationToken ct = default);

    /// <summary>Clear the gate immediately. Admin override; not needed in normal flow.</summary>
    Task ClearGate(string hostTag, CancellationToken ct = default);

    /// <summary>All currently active gates. Powers admin / observability surfaces.</summary>
    Task<IReadOnlyList<RateGateEntry>> GetActiveGates(CancellationToken ct = default);
}
