namespace Koan.Jobs;

/// <summary>Fully-resolved per-action policy: the <c>[JobAction]</c> values merged with the option defaults
/// (computed once, cached on the binding). Drives retry, lane/concurrency, timeout, scheduling, and the runaway guards.</summary>
internal sealed record ResolvedActionPolicy(
    string Action,
    string Lane,
    int MaxAttempts,
    int MaxConcurrency,
    OnFailure OnFailure,
    TimeSpan? Timeout,
    string? Schedule,
    TimeSpan Deadline,
    int MaxReschedules);
