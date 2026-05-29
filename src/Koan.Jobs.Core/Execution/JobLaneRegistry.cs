using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Options;
using Microsoft.Extensions.Options;

namespace Koan.Jobs.Execution;

/// <summary>
/// Holds one bounded async gate (<see cref="SemaphoreSlim"/>) per concurrency lane (JOBS-0002), and
/// optionally a second tier of per-partition gates within a lane (JOBS-0004). The dispatcher acquires
/// the permit(s) around the actual job-body run only, so independent lanes run in parallel, each is
/// capped, and a deferring job releases immediately. Lane caps come from
/// <c>Koan:Jobs:Lanes:{name}:MaxConcurrency</c> (falling back to
/// <see cref="JobsOptions.DefaultLaneConcurrency"/>). When a lane sets
/// <c>MaxConcurrencyPerPartition</c>, a job's <c>LanePartition</c> is gated too — acquired BEFORE the
/// lane-global permit so a hot partition's waiters never occupy global slots and starve other
/// partitions. The lane name and partition key are decided per-type by the job (JOBS-0003); this
/// registry only enforces caps.
/// </summary>
internal sealed class JobLaneRegistry
{
    private readonly JobsOptions _options;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string Lane, string Partition), SemaphoreSlim> _partitionGates = new();

    public JobLaneRegistry(IOptions<JobsOptions> options) => _options = options.Value;

    /// <summary>The configured global concurrency cap for a lane (>= 1).</summary>
    public int CapacityFor(string lane)
    {
        if (lane is not null && _options.Lanes.TryGetValue(lane, out var opt) && opt.MaxConcurrency > 0)
            return opt.MaxConcurrency;
        return _options.DefaultLaneConcurrency > 0 ? _options.DefaultLaneConcurrency : 1;
    }

    /// <summary>The per-partition concurrency cap for a lane+key, or 0 when partitioning is off for
    /// the lane (in which case only the global cap applies). An explicit
    /// <see cref="JobLaneOptions.PartitionOverrides"/> entry wins over the lane default.</summary>
    public int PartitionCapacityFor(string lane, string partition)
    {
        if (lane is null || !_options.Lanes.TryGetValue(lane, out var opt))
            return 0;
        if (opt.PartitionOverrides.TryGetValue(partition, out var ov) && ov > 0)
            return ov;
        return opt.MaxConcurrencyPerPartition > 0 ? opt.MaxConcurrencyPerPartition : 0;
    }

    /// <summary>Acquire a lane permit only (no partition tier). Dispose to release.</summary>
    public Task<IDisposable> AcquireAsync(string? lane, CancellationToken cancellationToken)
        => AcquireAsync(lane, null, cancellationToken);

    /// <summary>Acquire a permit for the lane and — when the lane configures a per-partition cap and
    /// <paramref name="partition"/> is non-empty — the per-partition permit too. The partition permit
    /// is taken FIRST (the same order for every job, so no deadlock) so a hot partition's waiters do
    /// not occupy lane-global slots. Dispose releases both. Awaits if at capacity.</summary>
    public async Task<IDisposable> AcquireAsync(string? lane, string? partition, CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(lane) ? JobLanes.Default : lane!;
        var laneGate = _gates.GetOrAdd(name, key =>
        {
            var cap = CapacityFor(key);
            return new SemaphoreSlim(cap, cap);
        });

        var partitionCap = string.IsNullOrWhiteSpace(partition) ? 0 : PartitionCapacityFor(name, partition!);
        if (partitionCap <= 0)
        {
            // No partition tier configured: lane-global only (unchanged behaviour).
            await laneGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Releaser(laneGate);
        }

        var partitionGate = _partitionGates.GetOrAdd(
            (name, partition!),
            static (_, cap) => new SemaphoreSlim(cap, cap),
            partitionCap);

        await partitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await laneGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            partitionGate.Release();
            throw;
        }
        return new Releaser(partitionGate, laneGate);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _partition;
        private SemaphoreSlim? _lane;

        // first = the gate taken first (the partition gate, or the lane gate when there is no
        // partition tier); second = the lane gate when a partition tier was used.
        public Releaser(SemaphoreSlim first, SemaphoreSlim? second = null)
        {
            if (second is null) { _lane = first; }
            else { _partition = first; _lane = second; }
        }

        public void Dispose()
        {
            // Release lane-global first, then partition (reverse of acquisition).
            var lane = Interlocked.Exchange(ref _lane, null);
            lane?.Release();
            var partition = Interlocked.Exchange(ref _partition, null);
            partition?.Release();
        }
    }
}
