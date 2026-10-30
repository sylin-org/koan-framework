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

    /// <summary>Cancel the work item's jobs (JOBS-0010): the running ones are marked first and the queued ones cancelled
    /// after, since a chain's successor is queued before its predecessor settles. A run marked in time stops its successor
    /// itself; a run that settled first has its successor queued for the second read.</summary>
    public async Task CancelWorkAsync(string workType, string workId, CancellationToken ct)
    {
        var item = JobDataRoute.From(EntityContext.Current).Query(workType, workId);
        var now = _clock.GetUtcNow();
        await CancelEach(item with { Status = JobStatus.Running }, now, ct);
        await CancelEach(item with { Status = JobStatus.Queued }, now, ct);
    }

    /// <summary>Cancel every job of one status, a bounded page at a time. A cancelled queued job leaves the queued set, so
    /// a page is read again until it holds only handled jobs — which inside a transaction, where the writes wait for the
    /// commit, moves on to the next page; a marked running job stays, so its pages advance the same way.</summary>
    private async Task CancelEach(JobQuery query, DateTimeOffset now, CancellationToken ct)
    {
        var handled = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 1; ;)
        {
            var rows = await _ledger.Query(query with { Page = page, Size = Constants.Submission.CancelPageSize }, ct);
            var fresh = 0;
            foreach (var rec in rows)
            {
                if (!handled.Add(rec.Id)) continue;
                fresh++;
                await _orchestrator.CancelAsync(rec, now, ct);
            }
            if (rows.Count < Constants.Submission.CancelPageSize) return;
            if (fresh == 0) page++;
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
    /// the ledger has since evicted or purged still reports the outcome it reached. An outcome nobody can read any more has
    /// no status, and is never a failure (JOBS-0010).</summary>
    public JobHandle Handle(string jobId) => new(jobId, async (timeout, ct) =>
    {
        using var watch = _completions.Hear(jobId);
        var deadline = _clock.GetUtcNow() + timeout;
        while (true)
        {
            var r = await _ledger.Get(jobId, ct);
            if (watch.Heard.IsCompleted) return await watch.Heard;
            if (r is null)
                return new JobOutcome(null, "The ledger does not hold this job: it was never submitted, or it settled and " +
                                            "was evicted or purged before its outcome was read.");
            if (r.IsTerminal) return new JobOutcome(r.Status, r.LastError);
            if (_clock.GetUtcNow() >= deadline) return new JobOutcome(r.Status);
            await Task.WhenAny(watch.Heard, Task.Delay(TimeSpan.FromMilliseconds(50), _clock, ct));
            ct.ThrowIfCancellationRequested();
        }
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
