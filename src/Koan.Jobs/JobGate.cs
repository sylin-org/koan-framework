namespace Koan.Jobs;

/// <summary>
/// A shared resource gate (cooperative-backoff circuit-breaker, JOBS-0005 §6.5). When set (by <c>ctx.Backoff</c>
/// on a 429), the orchestrator defers — at dispatch, without running — every job whose gate key matches, until
/// <see cref="ReleaseAt"/>. Graded like the ledger: in-memory locally, a shared record when durable, so the
/// cooldown is honored across all nodes.
/// </summary>
public sealed class JobGate
{
    public string GateKey { get; set; } = "";
    public DateTimeOffset ReleaseAt { get; set; }
    public string? Reason { get; set; }
}
