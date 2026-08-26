using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Jobs;

/// <summary>
/// One entry in the worker fleet roster (PMC-055): a live Jobs node, keyed by the orchestrator's
/// owner identity. Heartbeats bump <see cref="LastSeenAt"/>; the reaper treats a worker whose
/// entry is older than <c>JobsOptions.WorkerDeathTimeout</c> as confirmed dead and may reclaim its
/// running jobs before their lease lapses — safe because the revived node's lease renewal fails and
/// it abandons without settling (JOBS-0009). Ambient-exempt like the ledger: the roster is
/// control-plane state visible from any tenant scope.
/// </summary>
public sealed class WorkerNode : Entity<WorkerNode>, IAmbientExempt
{
    /// <summary>Last heartbeat — the entire liveness signal.</summary>
    [Index]
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>When this worker joined the fleet.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Host machine, for humans reading the roster.</summary>
    public string Machine { get; set; } = "";
}
