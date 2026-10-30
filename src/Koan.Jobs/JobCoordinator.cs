using System.Diagnostics;
using Koan.Core.Context;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Jobs.Infrastructure;
using Koan.Jobs.Semantics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Jobs;

/// <inheritdoc/>
internal sealed class JobCoordinator : IJobCoordinator
{
    private readonly IJobLedger _ledger;
    private readonly JobTypeRegistry _registry;
    private readonly JobOrchestrator _orchestrator;
    private readonly JobWakeCoordinator _wake;
    private readonly IServiceProvider _services;
    private readonly JobsOptions _options;
    private readonly TimeProvider _clock;
    private readonly JobsContextPlan _contextPlan;
    private readonly JobCompletions _completions;

    public JobCoordinator(IJobLedger ledger, JobTypeRegistry registry, JobOrchestrator orchestrator,
        JobWakeCoordinator wake, IServiceProvider services, IOptions<JobsOptions> options, TimeProvider clock,
        JobsContextPlan contextPlan, JobCompletions completions)
    {
        _completions = completions;
        _ledger = ledger;
        _registry = registry;
        _orchestrator = orchestrator;
        _wake = wake;
        _services = services;
        _options = options.Value;
        _clock = clock;
        _contextPlan = contextPlan;
    }

    /// <summary>Resolve a job's gate key at submit. A property-based <c>[JobGate]</c> is read inline; a method-based
    /// resolver (§18) runs inside a DI scope so it can use scoped services (or load a related entity).</summary>
    private async Task<string?> ResolveGateKey(JobTypeBinding binding, object workItem, CancellationToken ct)
    {
        if (!binding.HasGateResolver) return binding.GateKey(workItem);
        using var scope = _services.CreateScope();
        return await binding.ResolveGateKey(workItem, scope.ServiceProvider, ct);
    }

    public async Task<JobHandle> SubmitAsync(object workItem, string action, TimeSpan? after, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        var scope = CaptureSubmission(workItem.GetType());
        var accepted = await Accept(workItem, action, after, scope, ct).ConfigureAwait(false);
        if (!accepted.Coalesced)
        {
            await Announce(scope.PendingCommit, ct).ConfigureAwait(false);
        }

        return accepted.Handle;
    }

    public async Task<JobSubmission> SubmitSourceAsync<T>(
        IAsyncEnumerable<T> workItems,
        string action,
        CancellationToken ct)
        where T : Entity<T>, IKoanJob<T>
    {
        ArgumentNullException.ThrowIfNull(workItems);

        // One immutable operation snapshot for the complete source. Context is captured before the first await and
        // never rediscovered per item, so a logical submission cannot drift between tenants/subjects mid-stream.
        var scope = CaptureSubmission(typeof(T));
        var enumerated = 0L;
        var submitted = 0L;
        var coalesced = 0L;
        var failed = 0L;
        var pendingWake = 0;
        var sourceCompleted = false;
        var enumerating = true;
        var currentAccepted = false;
        var wakeInterval = _options.Mode == JobMode.Inline
            ? 1
            : Constants.Submission.WakeInterval;

        JobSubmission Snapshot()
            => new(
                typeof(T).FullName!,
                action,
                enumerated,
                submitted,
                coalesced,
                failed,
                sourceCompleted,
                scope.PendingCommit);

        async Task Flush(CancellationToken cancellationToken)
        {
            if (pendingWake == 0)
            {
                return;
            }

            await Announce(scope.PendingCommit, cancellationToken).ConfigureAwait(false);
            pendingWake = 0;
        }

        void SignalAcceptedPrefix()
        {
            if (pendingWake > 0 && !scope.PendingCommit)
            {
                _wake.Notify();
            }
        }

        try
        {
            // Re-establish the terminal's captured logical context for deferred source enumeration and every
            // work-item save. A source cannot accidentally make one submission drift onto the worker flow's axes.
            using var dataScope = KoanContext.Push(scope.DataContext);
            using var contextScope = _contextPlan.RestoreForSubmit(typeof(T), scope.Carrier);
            await foreach (var workItem in workItems.WithCancellation(ct).ConfigureAwait(false))
            {
                enumerating = false;
                currentAccepted = false;
                enumerated++;

                var accepted = await Accept(workItem, action, null, scope, ct).ConfigureAwait(false);
                if (accepted.Coalesced)
                {
                    coalesced++;
                }
                else
                {
                    submitted++;
                    pendingWake++;
                }

                currentAccepted = true;
                if (pendingWake >= wakeInterval)
                {
                    await Flush(ct).ConfigureAwait(false);
                }

                enumerating = true;
            }

            sourceCompleted = true;
            await Flush(ct).ConfigureAwait(false);
            return Snapshot();
        }
        catch (JobSubmissionException)
        {
            throw;
        }
        catch (JobSubmissionCanceledException)
        {
            throw;
        }
        catch (OperationCanceledException error) when (ct.IsCancellationRequested)
        {
            if (!enumerating && !currentAccepted)
            {
                failed++;
            }

            SignalAcceptedPrefix();
            throw new JobSubmissionCanceledException(
                $"Job submission for '{typeof(T).Name}' was canceled; the summary reports the confirmed accepted prefix.",
                Snapshot(),
                error,
                ct);
        }
        catch (Exception error)
        {
            if (!enumerating && !currentAccepted)
            {
                failed++;
            }

            SignalAcceptedPrefix();
            var failure = enumerating && !sourceCompleted
                ? JobSubmissionException.FailureKind.SourceFailed
                : JobSubmissionException.FailureKind.SubmissionFailed;
            var message = failure == JobSubmissionException.FailureKind.SourceFailed
                ? $"The job Entity source '{typeof(T).Name}' failed after {submitted + coalesced} confirmed acceptance(s)."
                : $"Jobs could not accept '{typeof(T).Name}' at source ordinal {Math.Max(0, enumerated - 1)}. " +
                  "Inspect the inner error and ledger readiness; the summary reports the confirmed prefix only.";
            throw new JobSubmissionException(failure, message, Snapshot(), error);
        }
    }

    public async Task<JobHandle> TriggerAsync(string workType, string action, CancellationToken ct)
    {
        var binding = _registry.Require(workType);
        var policy = binding.ResolvePolicy(action, _options);
        var now = _clock.GetUtcNow();
        // The singleton is ephemeral — it must NOT be persisted into the consumer's entity collection. The orchestrator
        // creates a fresh instance via binding.NewSingleton at execution time (no store round-trip on the hot path).
        var workItem = binding.NewSingleton(Constants.Work.SingletonId);
        var carrier = _contextPlan.Capture(binding.ClrType);
        var route = JobDataRoute.From(EntityContext.Current);

        var coalesceKey = JobCoalesce.FoldAmbient(binding.CoalesceKey(workItem, action), carrier, route);
        if (coalesceKey is not null && await _ledger.FindCoalescing(binding.WorkType, coalesceKey, ct) is { } queued)
            return Handle(queued.Id);

        var gateKey = await ResolveGateKey(binding, workItem, ct);
        var rec = JobRecordFactory.Create(binding, policy, workItem, Constants.Work.SingletonId, action, now, null, Correlation(), gateKey, carrier, route);
        if (await _ledger.AppendCoalescing(rec, ct) is { } holder) return Handle(holder.Id);

        if (!EntityContext.InTransaction) _wake.Notify();
        if (_options.Mode == JobMode.Inline && !EntityContext.InTransaction) await _orchestrator.DrainAsync(ct);
        return Handle(rec.Id);
    }

    /// <summary>Cancel the work item's jobs until the item is quiet (JOBS-0010). Each pass marks the running jobs first
    /// and then cancels the queued ones, held for a chain predecessor or not — so a successor released by a completion
    /// a mark lost to is queued when the second half of the pass reads it — and the passes repeat, a bounded number of
    /// times, until one finds no job of the item it has not handled yet. A step that started running in between is
    /// marked, and its handler hears it (on this node at once, elsewhere at its next renewal).</summary>
    public async Task CancelWorkAsync(string workType, string workId, CancellationToken ct)
    {
        var item = JobDataRoute.From(EntityContext.Current).Query(workType, workId);
        var now = _clock.GetUtcNow();
        var handled = new HashSet<string>(StringComparer.Ordinal);
        for (var pass = 1; pass <= Constants.Submission.CancelPasses; pass++)
        {
            var acted = await CancelEach(item with { Status = JobStatus.Running }, handled, now, ct)
                        + await CancelEach(item with { Status = JobStatus.Queued }, handled, now, ct);
            if (acted == 0) return;
        }
    }

    /// <summary>Hand every job of one status to <see cref="CancelOne"/>, a bounded page at a time; returns how many this
    /// call had not handled before. A cancelled queued job leaves the queued set, so a page is read again until it holds
    /// only handled jobs — which inside a transaction, where the writes wait for the commit, moves on to the next page;
    /// a marked running job stays, so its pages advance the same way.</summary>
    private async Task<int> CancelEach(JobQuery query, HashSet<string> handled, DateTimeOffset now, CancellationToken ct)
    {
        var acted = 0;
        for (var page = 1; ;)
        {
            var rows = await _ledger.Query(query with { Page = page, Size = Constants.Submission.CancelPageSize }, ct);
            var fresh = 0;
            foreach (var rec in rows)
            {
                if (!handled.Add(rec.Id)) continue;
                fresh++;
                await CancelOne(rec, now, ct, handled);
            }
            acted += fresh;
            if (rows.Count < Constants.Submission.CancelPageSize) return acted;
            if (fresh == 0) page++;
        }
    }

    /// <summary>Cancel one job, landing on whatever state it is in when the write lands (JOBS-0010): a queued job — held
    /// for its chain predecessor or not — is cancelled, a running one is marked and its handler on this node signalled,
    /// and a settled one is left alone. Each write is a compare on the revision read; a miss reads the job again, a
    /// bounded number of times, so a cancellation that meets a requeue already landed still cancels. A job found settled
    /// after a miss may have released the chain successor its run appended: that successor is cancelled or marked at
    /// once, rather than left for the next pass.</summary>
    private async Task CancelOne(JobRecord? current, DateTimeOffset now, CancellationToken ct, HashSet<string> handled, int depth = 0)
    {
        var id = current?.Id;
        for (var attempt = 0; attempt <= Constants.Submission.CancelRereads && current is not null; attempt++)
        {
            switch (current.Status)
            {
                case JobStatus.Queued:
                    current.CancelRequestedAt = now;
                    current.HeldBy = null;
                    current.ReleaseCoalesceSlot();
                    current.Transitions.Add(new JobTransition { At = now, From = current.Status, To = JobStatus.Cancelled, Note = "cancelled (pre-run)" });
                    current.Status = JobStatus.Cancelled;
                    current.LastSettledAt = now;
                    if (await _ledger.Update(current, ct)) return;
                    break;
                case JobStatus.Running:
                    if (current.CancelRequestedAt is null)
                    {
                        current.CancelRequestedAt = now;
                        if (!await _ledger.Update(current, ct)) break;
                    }
                    _orchestrator.SignalCancel(current.Id);
                    return;
                default:
                    if (attempt > 0 && depth < Constants.Submission.CancelPasses
                        && current.SettledBy is { } run && current.Status is JobStatus.Completed or JobStatus.Failed)
                    {
                        var successor = JobRecord.SuccessorId(current.Id, run);
                        if (handled.Add(successor))
                            await CancelOne(await _ledger.Get(successor, ct), now, ct, handled, depth + 1);
                    }
                    return;
            }
            current = await _ledger.Get(id!, ct);
        }
    }

    /// <summary>The status of the work item's newest job: one row read newest first (JOBS-0010).</summary>
    public async Task<JobStatus?> StatusAsync(string workType, string workId, CancellationToken ct)
    {
        var newest = await _ledger.Query(JobDataRoute.From(EntityContext.Current).Query(workType, workId) with { Size = 1 }, ct);
        return newest.Count == 0 ? null : newest[0].Status;
    }

    public Task<IReadOnlyList<JobRecord>> WhereAsync(JobQuery query, CancellationToken ct) => _ledger.Query(query, ct);

    /// <summary>A handle that reads its job's outcome from the ledger and hears a settlement this process makes, so a job
    /// the ledger has since evicted or purged still reports the outcome it reached. A submission coalesced into another
    /// job is followed to the job that serves it. An outcome nobody can read any more is unknown, never a failure
    /// (JOBS-0010).</summary>
    public JobHandle Handle(string jobId) => new(jobId, async (timeout, ct) =>
    {
        using var watch = _completions.Hear(jobId);
        var deadline = _clock.GetUtcNow() + timeout;
        for (var hops = 0; ;)
        {
            if (watch.Heard.IsCompleted) return Settled(jobId, await watch.Heard);
            var following = watch.Following;
            var r = await _ledger.Get(following, ct);
            if (r is { IsTerminal: true, ServedBy: { } serving })
            {
                if (++hops > Constants.Work.MaxServedHops)
                    return new JobOutcome.Unknown($"Job {jobId} was served by a chain of more than {Constants.Work.MaxServedHops} coalesced jobs.");
                watch.Follow(following, serving);
                continue;
            }
            if (r is { IsTerminal: true }) return new JobOutcome.Settled(r.Status, r.LastError, ServedBy(jobId, following));
            if (r is null)
            {
                if (watch.Heard.IsCompleted) return Settled(jobId, await watch.Heard);
                // A job coalesced into another moves the watch on before the ledger can evict it: follow it.
                if (!string.Equals(watch.Following, following, StringComparison.Ordinal)) continue;
                return new JobOutcome.Unknown("The ledger does not hold this job: it was never submitted, or it settled and " +
                                              "was evicted or purged before its outcome was read.");
            }
            if (_clock.GetUtcNow() >= deadline) return new JobOutcome.TimedOut(r.Status, ServedBy(jobId, following));
            await Task.WhenAny(watch.Heard, Task.Delay(TimeSpan.FromMilliseconds(50), _clock, ct));
            ct.ThrowIfCancellationRequested();
        }

        static JobOutcome Settled(string jobId, JobCompletions.Settlement heard)
            => new JobOutcome.Settled(heard.Status, heard.Error, ServedBy(jobId, heard.JobId));

        static string? ServedBy(string jobId, string settledBy)
            => string.Equals(jobId, settledBy, StringComparison.Ordinal) ? null : settledBy;
    });

    private static string? Correlation() => Activity.Current?.TraceId.ToString();

    private SubmissionScope CaptureSubmission(Type workType)
        => new(
            _clock.GetUtcNow(),
            Correlation(),
            _contextPlan.Capture(workType),
            EntityContext.Current ?? new EntityContext.ContextState(),
            EntityContext.InTransaction);

    private async Task<AcceptedSubmission> Accept(
        object workItem,
        string action,
        TimeSpan? after,
        SubmissionScope scope,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        using var dataScope = KoanContext.Push(scope.DataContext);
        using var contextScope = _contextPlan.RestoreForSubmit(workItem.GetType(), scope.Carrier);
        var route = JobDataRoute.From(scope.DataContext);
        var binding = _registry.Require(workItem.GetType().FullName!);
        var policy = binding.ResolvePolicy(action, _options);
        var workId = binding.GetId(workItem);

        // Coalesce before any Entity save. The captured ambient participates in the key, so distinct logical
        // contexts cannot collapse onto one another merely because a source changes flow while enumerating.
        var coalesceKey = JobCoalesce.FoldAmbient(binding.CoalesceKey(workItem, action), scope.Carrier, route);
        if (coalesceKey is not null
            && await _ledger.FindCoalescing(binding.WorkType, coalesceKey, ct).ConfigureAwait(false) is { } queued)
        {
            return new AcceptedSubmission(Handle(queued.Id), Coalesced: true);
        }

        // A new work Entity is written before the record that points at it; a stored one is never overwritten, so a
        // write another writer committed after the caller read its copy stands (JOBS-0010). A failed ledger call is not
        // counted as accepted; retrying the failed item is the corrective path, with declared idempotency when effects
        // demand it.
        await binding.Insert(workItem, ct).ConfigureAwait(false);

        var gateKey = await ResolveGateKey(binding, workItem, ct).ConfigureAwait(false);
        var record = JobRecordFactory.Create(
            binding,
            policy,
            workItem,
            workId,
            action,
            scope.Now,
            after,
            scope.Correlation,
            gateKey,
            scope.Carrier,
            route);
        // The coalescing check above skips the work-item write for a sequential duplicate; this append is the atomic
        // check, so concurrent duplicates coalesce too (JOBS-0010).
        if (await _ledger.AppendCoalescing(record, ct).ConfigureAwait(false) is { } holder)
            return new AcceptedSubmission(Handle(holder.Id), Coalesced: true);
        return new AcceptedSubmission(Handle(record.Id), Coalesced: false);
    }

    private async Task Announce(bool pendingCommit, CancellationToken ct)
    {
        // Transactional outbox: the work-item Save and ledger Append are not visible until commit. The poll remains
        // the correctness path, so Jobs must not publish a premature hint or inline-drain uncommitted rows.
        if (pendingCommit)
        {
            return;
        }

        _wake.Notify();
        if (_options.Mode == JobMode.Inline)
        {
            await _orchestrator.DrainAsync(ct).ConfigureAwait(false);
        }
    }

    private sealed record SubmissionScope(
        DateTimeOffset Now,
        string? Correlation,
        IReadOnlyDictionary<string, string>? Carrier,
        EntityContext.ContextState DataContext,
        bool PendingCommit);

    private readonly record struct AcceptedSubmission(JobHandle Handle, bool Coalesced);
}
