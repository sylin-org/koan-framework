using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Jobs;

/// <summary>
/// A shared resource gate (cooperative-backoff circuit-breaker, JOBS-0005 §6.5): set by <c>ctx.Backoff</c> on a
/// 429-style trip, it defers — at dispatch, without running — every job whose gate key matches until
/// <see cref="ReleaseAt"/>. Graded like the ledger itself (PMC-060 unified the former read-shape POCO into this
/// single type): an in-memory row locally, a shared <see cref="Entity{T}"/> when durable, so the cooldown is
/// honored across all nodes. Not an application Entity — framework control-plane state like <see cref="JobRecord"/>.
/// </summary>
public sealed class JobGateRecord : Entity<JobGateRecord>, IAmbientExempt
{
    public string GateKey { get; set; } = "";
    public DateTimeOffset ReleaseAt { get; set; }
    public string? Reason { get; set; }
}
