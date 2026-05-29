using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Options;
using Microsoft.Extensions.Options;

namespace Koan.Jobs.Execution;

/// <summary>
/// Holds one bounded async gate (<see cref="SemaphoreSlim"/>) per concurrency lane (JOBS-0002). The
/// dispatcher acquires a lane permit around the actual job-body run only, so independent lanes run
/// in parallel and each is capped, while a deferring job releases its permit immediately. Caps come
/// from <c>Koan:Jobs:Lanes:{name}:MaxConcurrency</c>, falling back to
/// <see cref="JobsOptions.DefaultLaneConcurrency"/>. The lane name itself is decided per-type by the
/// job's <c>Lane</c> override (JOBS-0003) and stamped at enqueue; this registry only enforces caps.
/// </summary>
internal sealed class JobLaneRegistry
{
    private readonly JobsOptions _options;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    public JobLaneRegistry(IOptions<JobsOptions> options) => _options = options.Value;

    /// <summary>The configured concurrency cap for a lane (>= 1).</summary>
    public int CapacityFor(string lane)
    {
        if (lane is not null && _options.Lanes.TryGetValue(lane, out var opt) && opt.MaxConcurrency > 0)
            return opt.MaxConcurrency;
        return _options.DefaultLaneConcurrency > 0 ? _options.DefaultLaneConcurrency : 1;
    }

    /// <summary>Acquire a permit for the lane, awaiting if at capacity. Dispose to release.</summary>
    public async Task<IDisposable> AcquireAsync(string? lane, CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(lane) ? JobLanes.Default : lane!;
        var gate = _gates.GetOrAdd(name, key =>
        {
            var cap = CapacityFor(key);
            return new SemaphoreSlim(cap, cap);
        });
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(gate);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;
        public Releaser(SemaphoreSlim gate) => _gate = gate;
        public void Dispose()
        {
            var gate = Interlocked.Exchange(ref _gate, null);
            gate?.Release();
        }
    }
}
