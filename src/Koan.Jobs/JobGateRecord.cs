using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Jobs;

/// <summary>
/// Durable form of a resource gate (<see cref="JobGate"/>) — a parallel <see cref="Entity{T}"/> set so cooperative
/// backoff (429 cooldowns) is honored across all nodes, not just the one that hit the limit (JOBS-0005 §6.5/§8).
/// </summary>
public sealed class JobGateRecord : Entity<JobGateRecord>, IAmbientExempt
{
    public string GateKey { get; set; } = "";
    public DateTimeOffset ReleaseAt { get; set; }
    public string? Reason { get; set; }
}
