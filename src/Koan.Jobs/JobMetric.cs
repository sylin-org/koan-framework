using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Jobs;

/// <summary>
/// A worker-batched, node-sharded throughput rollup (JOBS-0005 §20.2). Each orchestrator accumulates terminal
/// outcomes in memory and periodically flushes its <em>own</em> shard row — so dashboards read pre-aggregated counts
/// in O(shards) that <strong>survive retention</strong> (Tier 1 deletes the JobRecords; these counts remain). The
/// ledger stays the source of truth; this rollup is derived and lossy-tolerant. Opt-in via
/// <see cref="JobsOptions.MetricsEnabled"/>.
/// </summary>
public sealed class JobMetric : Entity<JobMetric>, IAmbientExempt
{
    /// <summary>Time bucket "yyyy-MM-ddTHH" (hourly) — lexicographic order is chronological.</summary>
    [Index(Group = "ix_metric", Order = 1)]
    public string Bucket { get; set; } = "";

    /// <summary>The work-item type (<see cref="JobRecord.WorkType"/>).</summary>
    [Index(Group = "ix_metric", Order = 0)]
    public string WorkType { get; set; } = "";

    /// <summary>Terminal outcome name: Completed | Failed | Cancelled | Dead.</summary>
    public string Outcome { get; set; } = "";

    /// <summary>The flushing node's shard (the orchestrator owner) — only that node writes this row, so the
    /// read-add-write flush is contention-free without an atomic increment.</summary>
    public string NodeShard { get; set; } = "";

    /// <summary>Cumulative count for (Bucket, WorkType, Outcome) contributed by this node.</summary>
    public long Count { get; set; }

    /// <summary>Last flush time — drives bucket-age retention.</summary>
    public DateTimeOffset LastFlushedAt { get; set; }

    /// <summary>Hourly bucket key for an instant.</summary>
    public static string BucketOf(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH");

    /// <summary>Stable row id for one node's shard of a (bucket, workType, outcome) cell.</summary>
    public static string KeyOf(string bucket, string workType, string outcome, string nodeShard)
        => $"{bucket}|{workType}|{outcome}|{nodeShard}";

    /// <summary>Throughput summary for a work-type over [from, to]: outcome → count summed across all shards/buckets.
    /// Reads the rollup (independent of ledger size, immune to retention), not the ledger.</summary>
    public static async Task<IReadOnlyDictionary<string, long>> Summary(
        string workType, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var fromB = BucketOf(from);
        var toB = BucketOf(to);
        // WorkType equality is pushed (indexed); the bucket-range + grouping run over the small per-work-type row set.
        var rows = await Query(m => m.WorkType == workType, ct);
        return rows
            .Where(r => string.CompareOrdinal(r.Bucket, fromB) >= 0 && string.CompareOrdinal(r.Bucket, toB) <= 0)
            .GroupBy(r => r.Outcome, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Count), StringComparer.Ordinal);
    }
}
