namespace Koan.Jobs;

/// <summary>
/// Thrown by code buried in a library that can't reach <see cref="JobContext"/>, to signal a cooperative
/// reschedule/backoff (the <c>ctx.Reschedule</c>/<c>ctx.Backoff</c> verbs are the idiom; this is the fallback).
/// The orchestrator treats it as a deferral that does NOT consume a retry attempt. JOBS-0005 §6.5.
/// </summary>
public sealed class RescheduleException : Exception
{
    /// <summary>Relative delay, if specified.</summary>
    public TimeSpan? After { get; }

    /// <summary>Absolute release time, if specified.</summary>
    public DateTimeOffset? Until { get; }

    /// <summary>When true, also set a shared resource gate (cooperative backoff for peers).</summary>
    public bool Gate { get; }

    /// <summary>Gate key override (null = use the job's declared gate key).</summary>
    public string? GateKey { get; }

    public RescheduleException(TimeSpan after, bool gate = false, string? gateKey = null)
        : base($"Job rescheduled after {after}.")
    {
        After = after;
        Gate = gate;
        GateKey = gateKey;
    }

    public RescheduleException(DateTimeOffset until)
        : base($"Job rescheduled until {until:O}.")
    {
        Until = until;
    }
}
