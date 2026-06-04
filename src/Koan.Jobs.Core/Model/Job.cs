using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Jobs.Execution;
using Koan.Jobs.Progress;
using Koan.Jobs.Support;
using TimestampAttribute = Koan.Data.Abstractions.Annotations.TimestampAttribute;

namespace Koan.Jobs.Model;

/// <summary>
/// CRTP base for a job (JOBS-0003): each concrete job is its own homogeneous <see cref="Entity{T}"/>
/// set (table-per-type). There is no shared abstract <c>Job</c> set and no discriminator, so
/// persistence works on every provider. This base carries the shared runtime substrate (status,
/// progress, timing, lane, coalesce, dependencies); concrete jobs add typed <c>Context</c>/<c>Result</c>
/// fields and implement <see cref="Do"/>.
/// </summary>
/// <typeparam name="T">The concrete job type (CRTP self-reference).</typeparam>
public abstract class Job<T> : Entity<T>, IKoanJob<T>
    where T : Job<T>, new()
{
    [Required]
    public JobStatus Status { get; set; } = JobStatus.Created;

    [Range(0.0, 1.0)]
    public double Progress { get; set; }

    [MaxLength(500)]
    public string? ProgressMessage { get; set; }

    public int? CurrentStep { get; set; }
    public int? TotalSteps { get; set; }
    public DateTimeOffset? EstimatedCompletion { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? QueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public TimeSpan? Duration { get; set; }

    /// <summary>Lease expiry stamped by the dispatcher when the job flips to <see cref="JobStatus.Running"/>
    /// and refreshed by an in-process heartbeat for the lifetime of the body. A non-terminal Running row
    /// whose lease has lapsed is treated as an orphan by <c>JobOrphanReaper</c> and reverted to
    /// <see cref="JobStatus.Queued"/> for re-dispatch. Null means "not currently leased" (the row is
    /// either pre-Running or already terminal).</summary>
    [Index]
    public DateTimeOffset? LeasedUntil { get; set; }

    [Index]
    [MaxLength(64)]
    public string? CorrelationId { get; set; }

    [MaxLength(2000)]
    public string? LastError { get; set; }

    /// <summary>Number of execution attempts made so far (incremented each dispatch). Drives the
    /// retry policy across delayed-visibility re-enqueues without a separate audit entity.</summary>
    public int Attempt { get; set; }

    /// <summary>Concurrency lane resolved at enqueue from <see cref="Lane"/> and stamped here so
    /// re-enqueues and observability preserve it (JOBS-0002).</summary>
    [Index]
    [MaxLength(100)]
    public string? ResolvedLane { get; set; }

    /// <summary>Idempotency key resolved at enqueue from <see cref="DeriveCoalesceKey"/> (JOBS-0002).
    /// A non-terminal job of this type with the same key is reused instead of minting a duplicate.</summary>
    [Index]
    [MaxLength(200)]
    public string? CoalesceKey { get; set; }

    /// <summary>Typed dependency references (ADR-0017 / JOBS-0003). The job will not start until
    /// each referenced job reaches a terminal state; a Failed/Cancelled dependency poisons it.</summary>
    public List<JobRef> WaitForRefs { get; set; } = new();

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object?> Metadata { get; set; } = new();

    [Timestamp(OnSave = true)]
    public DateTimeOffset LastModified { get; set; }

    // --- per-type policy (override as needed) -----------------------------------------------

    /// <summary>The concurrency lane this job runs in (JOBS-0002). Defaults to <see cref="JobLanes.Default"/>.</summary>
    protected internal virtual string Lane => JobLanes.Default;

    /// <summary>Optional partition key within <see cref="Lane"/> for the second concurrency tier
    /// (JOBS-0004). When the lane configures <c>MaxConcurrencyPerPartition</c>, jobs sharing a key are
    /// capped together and acquired partition-first, so one hot partition cannot fill the lane-global
    /// gate and starve the others. Null (the default) opts the job out of partitioning. Typically
    /// derived from the typed payload (e.g. an upstream brand).</summary>
    protected internal virtual string? LanePartition => null;

    /// <summary>Retry policy for this job type. Defaults to no retry.</summary>
    protected internal virtual RetryPolicyDescriptor Retry => RetryPolicyDescriptor.None;

    /// <summary>Optional coalesce key derived from this instance (typically from its typed payload).
    /// Null disables coalescing.</summary>
    protected internal virtual string? DeriveCoalesceKey() => null;

    /// <summary>Optional upstream host tag for the cross-job rate gate. Null bypasses the gate.</summary>
    protected internal virtual string? HostTag => null;

    // --- execution body ---------------------------------------------------------------------

    /// <summary>Run the job. Reads this instance's typed payload and writes its typed result.</summary>
    protected abstract Task Do(IJobProgress progress, CancellationToken cancellationToken);

    internal Task InvokeDo(IJobProgress progress, CancellationToken cancellationToken) => Do(progress, cancellationToken);

    /// <summary>Persist this instance to its own <see cref="Entity{T}"/> set. Used by the runtime
    /// (progress tracker, dispatcher) to write lifecycle without reflection.</summary>
    internal Task SaveSelf(CancellationToken cancellationToken) => Upsert((T)this, cancellationToken);

    // Internal accessors so the same-assembly runtime can read per-type policy uniformly.
    internal string LaneNameInternal => Lane;
    internal string? LanePartitionInternal => LanePartition;
    internal RetryPolicyDescriptor RetryInternal => Retry;
    internal string? HostTagInternal => HostTag;
    internal string? CoalesceKeyInternal() => DeriveCoalesceKey();

    // --- submission + lifecycle -------------------------------------------------------------

    private TimeSpan? _initialDelay;

    /// <summary>Add typed dependency references. Additive across calls. See ADR-0017.</summary>
    public T WaitFor(params JobRef[] refs)
    {
        if (refs is { Length: > 0 })
        {
            foreach (var r in refs)
            {
                if (!string.IsNullOrEmpty(r.Id) && !WaitForRefs.Contains(r))
                    WaitForRefs.Add(r);
            }
        }
        return (T)this;
    }

    /// <summary>Delay this job's first dispatch by <paramref name="delay"/> (JOBS-0002 delayed-visibility).</summary>
    public T After(TimeSpan delay)
    {
        if (delay > TimeSpan.Zero) _initialDelay = delay;
        return (T)this;
    }

    /// <summary>Persist this job and enqueue it for execution, returning the persisted handle.</summary>
    public Task<T> Submit(CancellationToken cancellationToken = default)
        => JobRuntime.Submit((T)this, _initialDelay, cancellationToken);

    /// <summary>Sugar: construct, configure, and submit a job in one call.</summary>
    public static Task<T> Push(Action<T>? configure = null, CancellationToken cancellationToken = default)
    {
        var job = new T();
        configure?.Invoke(job);
        return job.Submit(cancellationToken);
    }

    /// <summary>Reload this job's current persisted state.</summary>
    public async Task<T> Refresh(CancellationToken cancellationToken = default)
        => await Get(Id, cancellationToken) ?? (T)this;

    /// <summary>Poll until the job reaches a terminal state, returning the completed job or throwing.</summary>
    public async Task<T> Wait(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout ?? TimeSpan.FromMinutes(30));
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await Refresh(cancellationToken);
            switch (snapshot.Status)
            {
                case JobStatus.Completed: return snapshot;
                case JobStatus.Failed: throw new JobFailedException(snapshot.Id, snapshot.LastError);
                case JobStatus.Cancelled: throw new JobCancelledException(snapshot.Id);
            }
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException($"Job {Id} did not complete within the timeout.");
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    /// <summary>Request cancellation of this job.</summary>
    public Task Cancel(CancellationToken cancellationToken = default)
        => JobRuntime.Cancel<T>(Id, cancellationToken);

    /// <summary>Subscribe to progress updates for this job.</summary>
    public IDisposable OnProgress(Func<JobProgressUpdate, Task> handler, CancellationToken cancellationToken = default)
        => JobEnvironment.ProgressBroker.Subscribe(Id, handler, cancellationToken);
}
