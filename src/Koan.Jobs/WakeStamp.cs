using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Jobs.Infrastructure;

namespace Koan.Jobs;

/// <summary>
/// The framework-owned wake carriage (JOBS-0009, sharded by JOBS-0010): a fixed set of
/// <see cref="Constants.Wake.StampShards"/> rows per durable store. A durable append writes one shard, chosen at random,
/// with a fresh random version and no read, inside the same ambient transaction as the ledger records — so a
/// rolled-back submission never moves it, and concurrent submitting transactions meet on one row only when they pick the
/// same shard. Nodes probe the shards in one bounded read at short cadence and run the full claim scan only when the
/// fingerprint moved — or on the slow fallback timer regardless. A missed bump costs one <c>PollInterval</c>, never work:
/// polling remains the complete correctness mechanism.
/// </summary>
public sealed class WakeStamp : Entity<WakeStamp>, IAmbientExempt
{
    /// <summary>The id prefix every shard row shares; a shard's id is the prefix and its index.</summary>
    public const string ShardPrefix = "koan-jobs-wake-";

    /// <summary>A random non-zero value the bump that wrote this row chose.</summary>
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Move one shard per durable append batch. A failed bump is swallowed by design — it is a latency
    /// hint, not a correctness write; the probe's slow fallback drains on <c>PollInterval</c> anyway.</summary>
    internal static async Task TryBump(CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        try
        {
            var shard = Random.Shared.Next(Constants.Wake.StampShards);
            await Upsert(new WakeStamp
            {
                Id = ShardPrefix + shard,
                Version = Random.Shared.NextInt64(1, long.MaxValue),
                UpdatedAt = DateTimeOffset.UtcNow,
            }, ct);
        }
        catch (Exception)
        {
            // Deliberately narrow in purpose even though broad in type: any store failure here means the same
            // thing — peers discover work at their next full pass. Swallowing keeps the hint off the submit path.
        }
    }

    /// <summary>The fingerprint of every shard in one bounded read: 0 when no shard has been written, -1 when the read
    /// fails (callers treat unknown as no-signal). Any bump changes it.</summary>
    internal static async Task<long> ReadVersion(CancellationToken ct)
    {
        using var storageScope = JobsStorageScope.Enter();
        try
        {
            var fingerprint = 0L;
            foreach (var shard in await FirstPage(Constants.Wake.StampShards, ct)) fingerprint ^= shard.Version;
            return fingerprint;   // versions are positive, so the fingerprint never reads as the failure value
        }
        catch (Exception)
        {
            return -1;
        }
    }
}
