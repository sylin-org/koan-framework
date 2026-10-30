using System.Collections.Concurrent;
using Koan.Data.Core;
using Koan.Jobs.Semantics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Jobs;

/// <summary>
/// The one concern that claims, executes, settles, recalls, cancels, and advances jobs (JOBS-0005 §7). It talks only
/// to <see cref="IJobLedger"/> (storage-agnostic) and the <see cref="JobTypeRegistry"/> (bound handlers). Deterministic
/// by design: <see cref="DrainAsync"/> processes all currently-ready work to completion (the test driver); in production
/// the worker service claims through <see cref="TryStartNextAsync"/> and owns each execution as a background task
/// (JOBS-0010). All time comes from an injected <see cref="TimeProvider"/>.
/// </summary>
internal sealed class JobOrchestrator
{
    private readonly IJobLedger _ledger;
    private readonly JobTypeRegistry _registry;
    private readonly JobsOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<JobOrchestrator> _logger;
    private readonly IServiceScopeFactory _scopes;
    private readonly IReadOnlyList<IJobPoolResolver> _poolResolvers;
    private readonly JobsContextPlan _contextPlan;

    private readonly string _owner = Guid.CreateVersion7().ToString("N");
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _lanes = new(StringComparer.Ordinal);
    // JOBS-0010: keyed by claim token, so a zombie execution and its own node's re-claim of the job never share an entry.
    private readonly ConcurrentDictionary<string, (string JobId, CancellationTokenSource Execution)> _running = new(StringComparer.Ordinal);
    // JOBS-0010: the chain successor each running claim has appended, held for it, until a settlement closes it.
    private readonly ConcurrentDictionary<string, string> _heldSuccessors = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _unregisteredWorkTypeWarnings = new(StringComparer.Ordinal);
    private readonly JobMetricsRecorder _metrics;
    private readonly WorkItemWriter _writer;
    private DateTimeOffset _lastAssignAt = DateTimeOffset.MinValue;

    public JobOrchestrator(
        IJobLedger ledger, JobTypeRegistry registry, IOptions<JobsOptions> options,
        TimeProvider clock, ILogger<JobOrchestrator> logger, IServiceScopeFactory scopes,
        IEnumerable<IJobPoolResolver> poolResolvers, JobsContextPlan contextPlan)
    {
        _ledger = ledger;
        _registry = registry;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
        _scopes = scopes;
        _poolResolvers = poolResolvers.ToList();
        _contextPlan = contextPlan;
        _metrics = new JobMetricsRecorder(_options.MetricsEnabled, _owner, _clock);
        _writer = new WorkItemWriter(logger);

        // PMC-056 corrective boundary: an inline host executes jobs on the caller — there is no fleet roster to
        // reserve against, so reservation dispatch cannot be honored and must refuse rather than silently degrade.
        if (_options.DispatchMode == JobDispatchMode.Reservation && _options.Mode == JobMode.Inline)
            throw new InvalidOperationException(
                "Koan Jobs cannot honor DispatchMode.Reservation because the host runs Mode.Inline — submission " +
                "executes synchronously on the caller and there is no fleet roster to reserve against. Remove the " +
                "DispatchMode override, or set Mode.Normal.");
    }

    /// <summary>Fold this node's accumulated throughput deltas into its internal metric shard rows (§20.2).
    /// No-op unless <see cref="JobsOptions.MetricsEnabled"/>. Driven by the worker on <c>MetricsFlushInterval</c>.</summary>
    public Task FlushMetricsAsync(CancellationToken ct = default) => _metrics.FlushAsync(ct);

    public string Owner => _owner;

    /// <summary>Process every currently-ready job to completion, including chain follow-ons that become ready.
    /// Jobs with a future <c>VisibleAt</c> (delayed/deferred) are left until the clock advances. Deterministic.</summary>
    public async Task DrainAsync(CancellationToken ct = default)
    {
        var inflight = new List<Task>();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await AssignReservationsAsync(ct);

                // Global cap: don't claim more work than this node can run concurrently.
                if (_options.WorkerConcurrency > 0 && inflight.Count >= _options.WorkerConcurrency)
                {
                    await Task.WhenAny(inflight);
                    inflight.RemoveAll(t => t.IsCompleted);
                    continue;
                }

                if (await ClaimReadyAsync(ct) is { } work)
                {
                    inflight.Add(ExecuteAndReleaseAsync(work.Record, work.Binding, work.Policy, work.Lane, ct));
                    continue;
                }

                var settled = inflight.RemoveAll(t => t.IsCompleted);
                if (inflight.Count == 0)
                {
                    // A just-finished task may have appended a chain follow-on (SettleSuccess/Failure → Append). Don't
                    // conclude the drain is done until a fresh claim confirms nothing became ready: a successor enqueued
                    // in the window between the claim above and this check would otherwise be missed. The worker's poll
                    // loop hides this in production, but a single Drain on a higher-latency store (Mongo) exposes it.
                    if (settled > 0) continue;
                    break;
                }
                await Task.WhenAny(inflight);
                inflight.RemoveAll(t => t.IsCompleted);
            }
        }
        finally
        {
            await Task.WhenAll(inflight);
        }
    }

    /// <summary>Fire the cancellation token of every execution of a job running on this node (the durable marker is set
    /// by the caller).</summary>
    public void SignalCancel(string jobId)
    {
        foreach (var (_, run) in _running)
            if (string.Equals(run.JobId, jobId, StringComparison.Ordinal))
            {
                try { run.Execution.Cancel(); } catch (ObjectDisposedException) { }
            }
    }

    /// <summary>The worker's dispatch step (JOBS-0010): claim one ready job and start its execution in the background
    /// without awaiting it. The returned task never faults — execution and settlement failures are logged and settled
    /// inside it — and its handler observes <paramref name="executionCt"/>. Null when nothing is claimable.</summary>
    public async Task<Task?> TryStartNextAsync(CancellationToken executionCt, CancellationToken ct = default)
    {
        if (await ClaimReadyAsync(ct) is not { } work) return null;
        return Task.Run(() => ExecuteAndReleaseAsync(work.Record, work.Binding, work.Policy, work.Lane, executionCt), CancellationToken.None);
    }

    /// <summary>Claim the next ready job and take its lane slot. The claim already skipped saturated lanes, so the
    /// slot is free; the wait is defensive.</summary>
    private async Task<ClaimedWork?> ClaimReadyAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var pools = await ResolvePoolContextsAsync(ct);
        if (await ClaimBoundNextAsync(now, SaturatedLanes(), pools, ct) is not { } claimed) return null;
        var (rec, binding) = claimed;
        var policy = binding.ResolvePolicy(rec.Action, _options);
        var lane = LaneSem(policy.Lane, policy.MaxConcurrency);
        if (!lane.Wait(0)) await lane.WaitAsync(ct);
        return new ClaimedWork(rec, binding, policy, lane);
    }

    private sealed record ClaimedWork(JobRecord Record, JobTypeBinding Binding, ResolvedActionPolicy Policy, SemaphoreSlim Lane);

    private async Task ExecuteAndReleaseAsync(JobRecord rec, JobTypeBinding binding, ResolvedActionPolicy policy, SemaphoreSlim lane, CancellationToken workerCt)
    {
        try { await ExecuteClaimedAsync(rec, binding, policy, workerCt); }
        catch (Exception ex) { _logger.LogError(ex, "Unhandled error settling job {JobId}", rec.Id); }
        finally { lane.Release(); }
    }

    /// <summary>Claim and execute exactly one ready job through the same path as <see cref="DrainAsync"/>, returning
    /// its execution result. Returns null when nothing is ready or when the work-item could not be loaded.
    /// Designed for in-process stage-handler integration testing via <c>JobStagePilot</c>.</summary>
    public async Task<JobRunResult?> ExecuteNextAsync(CancellationToken ct = default)
    {
        if (await ClaimReadyAsync(ct) is not { } work) return null;

        var (rec, binding, policy, sem) = work;
        JobContext? ctx;
        try
        {
            try { ctx = await ExecuteClaimedAsync(rec, binding, policy, ct); }
            catch (Exception ex) { _logger.LogError(ex, "Unhandled error settling job {JobId}", rec.Id); ctx = null; }
        }
        finally { sem.Release(); }

        return ctx is null ? null
            : new JobRunResult(rec.Id, rec.WorkType, rec.Action, ctx.Signal, ctx.DeferUntil, ctx.NextAction, ctx.GateKeyOverride);
    }

    private async Task<JobContext?> ExecuteClaimedAsync(JobRecord rec, JobTypeBinding binding, ResolvedActionPolicy policy, CancellationToken workerCt)
    {
        // JOBS-0010: the token this execution's claim wrote is its authority for every owner write — renewal, progress,
        // settlement. An execution whose claim was taken over, on another node or on this one, carries a token the row
        // no longer holds, and every write it tries bounces.
        var token = rec.ClaimToken ?? throw new InvalidOperationException("Claimed job has no claim token.");
        // Restore the Koan context captured at submit BEFORE loading the (possibly tenant-scoped)
        // work-item, and keep them in scope across load + execute + settle (the conditional auto-save included) so
        // every tenant-scoped read/write runs in the submitted tenant. A restore failure (an unregistered axis, or
        // an unknown carrier format) is deterministic and non-retryable → dead-letter; the handler never runs
        // fail-open in a wrong/absent context. A null/empty bag explicitly suppresses every registered axis (the §1b
        // request guard owns the unscoped-write refusal under Closed; dev-fallback under Open).
        IDisposable ambientScope;
        IDisposable? dataScope = null;
        try
        {
            dataScope = JobDataRoute.From(rec).Restore();
            ambientScope = _contextPlan.RestoreForExecution(binding.ClrType, rec.AmbientCarrier);
        }
        catch (Exception ex)
        {
            dataScope?.Dispose();
            await SettleCarrierFailureAsync(rec, token, ex);
            return null;
        }
        using var _data = dataScope;
        using var _ambient = ambientScope;

        object? workItem;
        try { workItem = await binding.Load(rec.WorkId, workerCt); }
        catch (Exception ex) { await SettleFailureAsync(rec, binding, policy, ex, token); return null; }
        // Type-level triggers (TriggerAsync) use an ephemeral singleton that is never persisted; re-create it here.
        workItem ??= rec.WorkId == Infrastructure.Constants.Work.SingletonId ? binding.NewSingleton(rec.WorkId) : null;
        if (workItem is null) { await SettleFailureAsync(rec, binding, policy, new InvalidOperationException($"Work-item {rec.WorkType}/{rec.WorkId} not found."), token); return null; }
        Notify(rec.WorkType, JobEventKind.Claimed, workItem, rec);

        var loaded = WorkItemImage.Capture(workItem);   // the item as Koan stores it: what settle compares against (JOBS-0010)

        using var timeoutCts = policy.Timeout is { } to
            ? new CancellationTokenSource(to, _clock)
            : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(workerCt, timeoutCts.Token);
        _running[token] = (rec.Id, linked);
        if (rec.CancelRequestedAt is not null) { try { linked.Cancel(); } catch (ObjectDisposedException) { } }

        using var scope = _scopes.CreateScope();
        Task PersistProgress(string jobId, double fraction, string? message, CancellationToken ct)
        {
            // Progress is written for live observers, merged onto the row as stored; this execution's copy keeps its own
            // last value too, which its settlement writes (JOBS-0010).
            rec.ProgressFraction = fraction;
            rec.ProgressMessage = message;
            return _ledger.Progress(jobId, token, fraction, message, ct);
        }

        var ctx = new JobContext(rec.Action, rec.Id, scope.ServiceProvider, _logger, ToState(rec), _clock, linked.Token, PersistProgress);
        // JOBS-0009 lease heartbeat: hold the claim alive while the handler runs. Losing the lease means another
        // claimant owns the row — execution cancels and NOTHING settles (any write here would clobber that owner).
        var lostLease = 0;
        try
        {
            using (_ = StartLeaseRenewal(rec, token, linked, () => Volatile.Write(ref lostLease, 1), workerCt))
            {
                await binding.Execute(workItem, ctx, linked.Token);
            }
            if (Volatile.Read(ref lostLease) != 0)
            {
                _logger.LogWarning("Koan.Jobs abandoned job {JobId}/{Action} after losing its lease; another claimant owns the row and no settle was written.", rec.Id, rec.Action);
                Notify(rec.WorkType, JobEventKind.Abandoned, workItem, rec);
                return ctx;
            }
            // The success is decided on a copy of the run's row: a failure that ends its settlement is settled from the row
            // as the run left it, never from a completion that did not land.
            await SettleSuccessAsync(rec.Clone(), workItem, binding, policy, ctx, loaded, token);
        }
        catch (RescheduleException rex)
        {
            var until = rex.Until ?? _clock.GetUtcNow() + (rex.After ?? TimeSpan.Zero);
            await ApplyDeferralAsync(rec, binding, policy, until, rex.Gate, rex.Gate ? rex.GateKey : null, hasOverride: rex.Gate, "reschedule-exception", token);
        }
        catch (OperationCanceledException)
        {
            if (Volatile.Read(ref lostLease) != 0)
            {
                _logger.LogWarning("Koan.Jobs abandoned job {JobId}/{Action} after losing its lease; another claimant owns the row and no settle was written.", rec.Id, rec.Action);
                Notify(rec.WorkType, JobEventKind.Abandoned, workItem, rec);
            }
            else if (timeoutCts.IsCancellationRequested)
                await SettleFailureAsync(rec, binding, policy, new TimeoutException($"Action '{rec.Action}' exceeded its timeout."), token);
            else if (await IsCancelMarkerSet(rec.Id))
                await SettleCancelledAsync(rec, token);
            else
                await SettleShutdownAsync(rec, token); // worker stopping — revert for reclaim
        }
        catch (Exception ex)
        {
            await SettleFailureAsync(rec, binding, policy, ex, token);
        }
        finally
        {
            _running.TryRemove(token, out _);
            // A successor still held here met an end of claim whose outcome is unknown (the store failed under it): the
            // reaper decides it, by the settlement it was appended for.
            _heldSuccessors.TryRemove(token, out _);
        }
        return ctx;
    }

    /// <summary>JOBS-0009: renew the claim's lease on a derived cadence (LeaseDuration/3, 100ms floor) while the
    /// handler runs. A renewal that finds the claim gone means another claimant owns the row: signal
    /// <paramref name="onLost"/>, cancel the execution token so the handler stops at its next checkpoint, and stop. A
    /// renewal that reads a cancellation request — asked on any node — cancels the handler's token too (JOBS-0010). The
    /// loop swallows its own cancellation; transient renewal-write failures are retried next tick — the reaper remains
    /// the bound.</summary>
    private LeaseHeartbeat StartLeaseRenewal(JobRecord rec, string token, CancellationTokenSource execution, Action onLost, CancellationToken workerCt)
    {
        var duration = _options.LeaseDuration;
        if (duration <= TimeSpan.Zero || rec.Id is null)
            return default;

        var interval = TimeSpan.FromTicks(Math.Clamp(duration.Ticks / 3,
            TimeSpan.FromMilliseconds(100).Ticks, Infrastructure.Constants.Worker.LongestWait.Ticks));
        var cts = CancellationTokenSource.CreateLinkedTokenSource(workerCt);
        var timer = new PeriodicTimer(interval, _clock);
        var jobId = rec.Id;
        _ = RenewLoopAsync();

        async Task RenewLoopAsync()
        {
            try
            {
                while (await timer.WaitForNextTickAsync(cts.Token))
                {
                    JobRecord? renewed;
                    var now = _clock.GetUtcNow();
                    try { renewed = await _ledger.RenewLease(jobId, token, now + duration, cts.Token); }
                    catch (OperationCanceledException) when (cts.Token.IsCancellationRequested) { break; }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Koan.Jobs could not write the lease renewal for job {JobId}; retrying next tick.", jobId);
                        continue;
                    }
                    if (renewed is not null)
                    {
                        if (renewed.CancelRequestedAt is not null)
                        {
                            try { execution.Cancel(); } catch (ObjectDisposedException) { }
                        }
                        continue;
                    }
                    _logger.LogWarning("Koan.Jobs lost the lease for job {JobId}; cancelling execution so it can abandon without settling.", jobId);
                    onLost();
                    try { execution.Cancel(); } catch (ObjectDisposedException) { }
                    break;
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                timer.Dispose();
                cts.Dispose();
            }
        }

        return new LeaseHeartbeat(cts);
    }

    private readonly struct LeaseHeartbeat : IDisposable
    {
        private readonly CancellationTokenSource? _cts;
        public LeaseHeartbeat(CancellationTokenSource? cts) => _cts = cts;
        public void Dispose()
        {
            if (_cts is null) return;
            try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    // --- settle paths ---

    /// <summary>PMC-056: lifecycle events are pure projection — fired after the durable write, never
    /// on it, and a throwing observer can never affect settlement.</summary>
    private void Notify(string workType, JobEventKind kind, object? model, JobRecord rec, string? error = null)
    {
        try { JobEventRegistry.Publish(workType, kind, model, rec, error); }
        catch (Exception ex) { _logger.LogDebug(ex, "Job lifecycle observer for {WorkType} threw; treated as consumed.", rec.WorkType); }
    }

    /// <summary>End this execution's claim (PMC-055, JOBS-0010): the ledger merges its decision onto the row as stored,
    /// only while the row carries its claim token, and returns the row as written — a requeue the job was asked to cancel
    /// during comes back Cancelled. Null when the claim was taken over: another claim holds the row and nothing was
    /// written, so a revived zombie never clobbers its successor.</summary>
    private async Task<JobRecord?> EndClaimAsync(JobRecord rec, string token, string? releasing = null)
    {
        var written = rec.Status == JobStatus.Queued
            ? await _ledger.TryRequeue(rec, token, CancellationToken.None)
            : await _ledger.TrySettle(rec, token, CancellationToken.None);
        if (written is null)
            _logger.LogWarning(
                "Koan.Jobs abandoned settling job {JobId}: its claim was taken over (reclaimed); no write was made.",
                rec.Id);
        // An end of claim that lands closes the held successor it does not release: one a failure that aborts, or a
        // requeue, met after the success path appended it (JOBS-0010). One that did not land leaves it to the reaper, which
        // decides by the settlement it was held for: the completion it was appended for may have landed, its answer lost.
        if (_heldSuccessors.TryGetValue(token, out var appended) && !string.Equals(appended, releasing, StringComparison.Ordinal))
        {
            _heldSuccessors.TryRemove(token, out _);
            if (written is not null)
                await _ledger.DeleteHeld(appended, token, CancellationToken.None);
        }
        return written;
    }

    /// <summary>A requeue the ledger settled Cancelled, because the job was asked to cancel while it ran (JOBS-0010): the
    /// cancellation is what happened, so no gate is set and no reschedule is published.</summary>
    private void SettledCancelled(JobRecord written)
    {
        _metrics.Record(written.WorkType, JobStatus.Cancelled, written.LastSettledAt ?? _clock.GetUtcNow());
        Notify(written.WorkType, JobEventKind.Cancelled, null, written);
    }

    /// <summary>Append the chain successor this run settles into, held for it (JOBS-0010): queued but never claimed until
    /// this run's settlement releases it, under an id derived from this job and its claim token, so a run's appends write
    /// one row and two runs never collide.</summary>
    private async Task<string> AppendHeldSuccessorAsync(JobRecord rec, string token, JobTypeBinding binding, object workItem, string next,
        DateTimeOffset now, JobStatus heldOn)
    {
        var nextPolicy = binding.ResolvePolicy(next, _options);
        // Chain stages inherit the gate key resolved at submit (the chain's gate pool is fixed — §18) and the ambient
        // carrier (ARCH-0100 §7): the successor is appended by the orchestrator, NOT the coordinator, so
        // capture-at-submit never fires for it — propagate the parent's bag verbatim.
        var successor = JobRecordFactory.Create(binding, nextPolicy, workItem, rec.WorkId, next, now, null, rec.CorrelationId, rec.GateKey, rec.AmbientCarrier, JobDataRoute.From(rec));
        successor.Id = JobRecord.SuccessorId(rec.Id, token);
        successor.HeldBy = token;
        successor.HeldFor = rec.Id;
        successor.HeldOn = heldOn;
        await _ledger.Append(successor, CancellationToken.None);
        _heldSuccessors[token] = successor.Id;
        return successor.Id;
    }

    /// <summary>Close the chain by order (JOBS-0010): a settlement that landed releases this run's held successor, or
    /// cancels it when the row as settled carries a cancellation request. One that did not land leaves it to the reaper,
    /// which decides by what the row shows: a claim taken over, or an earlier end of this claim that landed with its answer
    /// lost.</summary>
    private async Task CloseSuccessorAsync(string? successorId, string token, JobRecord? written)
    {
        if (successorId is null) return;
        _heldSuccessors.TryRemove(token, out _);
        if (written is not null)
            await _ledger.ReleaseHeld(successorId, token, cancel: written.CancelRequestedAt is not null, _clock.GetUtcNow(), CancellationToken.None);
    }

    private async Task SettleSuccessAsync(JobRecord rec, object workItem, JobTypeBinding binding, ResolvedActionPolicy policy, JobContext ctx, WorkItemImage? loaded, string token)
    {
        if (ctx.Signal is JobSignal.Reschedule or JobSignal.Backoff)
        {
            var until = ctx.DeferUntil ?? _clock.GetUtcNow();
            await ApplyDeferralAsync(rec, binding, policy, until, ctx.Signal == JobSignal.Backoff, ctx.GateKeyOverride, ctx.GateKeyOverrideSet, "cooperative-backoff", token);
            return;
        }

        // Write back only what the handler changed on the reference it was given, merged onto the stored row (JOBS-0010):
        // a handler that worked on its own copy left this one clean, and a write another writer made while the job ran
        // stands. A member both changed is a conflict: the work is valid but stale, so the job runs again on the stored
        // item, and nothing settles or advances.
        try
        {
            await _writer.Persist(binding, rec.WorkId, workItem, loaded, CancellationToken.None);
        }
        catch (WorkItemConflictException conflict)
        {
            await SettleConflictAsync(rec, conflict, token);
            return;
        }

        var now = _clock.GetUtcNow();
        var next = ctx.Signal switch
        {
            JobSignal.ContinueWith => ctx.NextAction,
            JobSignal.StopChain => null,
            _ => binding.NextInChain(rec.Action),
        };
        var successorId = next is null ? null : await AppendHeldSuccessorAsync(rec, token, binding, workItem, next, now, JobStatus.Completed);

        rec.Owner = null;
        rec.LeaseUntil = null;
        rec.ReservedFor = null;
        rec.ReservedUntil = null;
        rec.LastError = null;
        rec.DeferReason = null;
        rec.LastSettledAt = now;
        rec.ExpireAt = ExpiryAt(_options.ArchiveAfter, now);
        SetStatus(rec, JobStatus.Completed, now, "completed");
        var written = await EndClaimAsync(rec, token, releasing: successorId);
        await CloseSuccessorAsync(successorId, token, written);
        if (written is null) return;
        _metrics.Record(rec.WorkType, JobStatus.Completed, now);
        Notify(rec.WorkType, JobEventKind.Completed, workItem, written);
    }

    private async Task SettleFailureAsync(JobRecord rec, JobTypeBinding binding, ResolvedActionPolicy policy, Exception ex, string token)
    {
        var now = _clock.GetUtcNow();
        rec.Owner = null;
        rec.LeaseUntil = null;
        rec.ReservedFor = null;
        rec.ReservedUntil = null;
        rec.LastError = ex.Message;
        rec.LastSettledAt = now;

        if (rec.Attempt < policy.MaxAttempts)
        {
            rec.VisibleAt = now + RetryDelay(rec.Attempt);
            SetStatus(rec, JobStatus.Queued, now, $"retry {rec.Attempt}/{policy.MaxAttempts}: {ex.GetType().Name}");
            if (await EndClaimAsync(rec, token) is { IsTerminal: true } cancelled) SettledCancelled(cancelled);
            return;
        }

        rec.DeadReason = DeadReason.Poison.ToString();
        rec.ExpireAt = ExpiryAt(_options.FailedAfter, now);
        SetStatus(rec, JobStatus.Failed, now, $"failed after {policy.MaxAttempts} attempts: {ex.Message}");
        string? successorId = null;
        // Under OnFailure.Continue the chain advances either way: a successor this run already holds — its success's end of
        // claim failed, and may have landed — is the one this failure releases, still held on the completion it was
        // appended for, so the reaper releases it should that completion be what landed.
        if (policy.OnFailure == OnFailure.Continue && _heldSuccessors.TryGetValue(token, out var held))
            successorId = held;
        else if (policy.OnFailure == OnFailure.Continue && binding.NextInChain(rec.Action) is { } next
            && await binding.Load(rec.WorkId, CancellationToken.None) is { } wi)
            successorId = await AppendHeldSuccessorAsync(rec, token, binding, wi, next, now, JobStatus.Failed);
        var written = await EndClaimAsync(rec, token, releasing: successorId);
        await CloseSuccessorAsync(successorId, token, written);
        if (written is null) return;
        _metrics.Record(rec.WorkType, JobStatus.Failed, now);
        Notify(rec.WorkType, JobEventKind.Failed, null, written, ex.Message);
    }

    /// <summary>The captured Koan context could not be restored (unknown axis, invalid format/version, or insufficient
    /// trust). Deterministic — retrying would fail identically — so dead-letter immediately rather than run the handler
    /// in a wrong/absent context. The work item is never loaded.</summary>
    private async Task SettleCarrierFailureAsync(JobRecord rec, string token, Exception ex)
    {
        var now = _clock.GetUtcNow();
        rec.Owner = null;
        rec.LeaseUntil = null;
        rec.ReservedFor = null;
        rec.ReservedUntil = null;
        rec.LastError = ex.Message;
        rec.LastSettledAt = now;
        rec.DeadReason = DeadReason.CarrierRestoreFailed.ToString();
        rec.ExpireAt = ExpiryAt(_options.FailedAfter, now);
        SetStatus(rec, JobStatus.Dead, now, $"ambient carrier restore failed: {ex.Message}");
        if (await EndClaimAsync(rec, token) is not { } written) return;
        _metrics.Record(rec.WorkType, JobStatus.Dead, now);
        Notify(rec.WorkType, JobEventKind.DeadLettered, null, written, ex.Message);
    }

    private async Task SettleUnregisteredWorkTypeAsync(JobRecord rec, string token)
    {
        var now = _clock.GetUtcNow();
        rec.Owner = null;
        rec.LeaseUntil = null;
        rec.ReservedFor = null;
        rec.ReservedUntil = null;
        rec.LastError = $"No job binding is registered for work type '{rec.WorkType}'.";
        rec.LastSettledAt = now;
        rec.DeadReason = DeadReason.UnregisteredWorkType.ToString();
        rec.ExpireAt = ExpiryAt(_options.FailedAfter, now);
        SetStatus(rec, JobStatus.Dead, now, $"unregistered work type: {rec.WorkType}");
        if (await EndClaimAsync(rec, token) is null) return;
        _metrics.Record(rec.WorkType, JobStatus.Dead, now);
        if (_unregisteredWorkTypeWarnings.TryAdd(rec.WorkType, 0))
            _logger.LogWarning(
                "Dead-lettered jobs for unregistered work type {WorkType}; the first observed job was {JobId}",
                rec.WorkType,
                rec.Id);
        else
            _logger.LogDebug(
                "Dead-lettered job {JobId} because work type {WorkType} is not registered",
                rec.Id,
                rec.WorkType);
    }

    private async Task ApplyDeferralAsync(JobRecord rec, JobTypeBinding binding, ResolvedActionPolicy policy,
        DateTimeOffset until, bool gate, string? gateKeyOverride, bool hasOverride, string reason, string token)
    {
        var now = _clock.GetUtcNow();
        // reschedule does NOT consume a retry attempt: undo the claim-time increment.
        rec.Attempt = Math.Max(0, rec.Attempt - 1);
        rec.Reschedules++;

        var deadlineHit = rec.Deadline is { } dl && now >= dl;
        var maxHit = policy.MaxReschedules >= 0 && rec.Reschedules > policy.MaxReschedules;
        rec.Owner = null;
        rec.LeaseUntil = null;
        rec.ReservedFor = null;
        rec.ReservedUntil = null;
        rec.LastSettledAt = now;

        if (deadlineHit || maxHit)
        {
            rec.DeadReason = DeadReason.PerpetuallyDeferred.ToString();
            rec.ExpireAt = ExpiryAt(_options.FailedAfter, now);
            SetStatus(rec, JobStatus.Dead, now, deadlineHit ? "deadline exceeded" : "max reschedules exceeded");
            if (await EndClaimAsync(rec, token) is not { } dead) return;
            _metrics.Record(rec.WorkType, JobStatus.Dead, now);
            Notify(rec.WorkType, JobEventKind.DeadLettered, null, dead);
            return;
        }

        rec.DeferReason = reason;
        rec.VisibleAt = ApplyJitter(until);
        SetStatus(rec, JobStatus.Queued, now, $"deferred to {rec.VisibleAt:O} ({reason})");
        if (await EndClaimAsync(rec, token) is not { } written) return;
        if (written.IsTerminal)
        {
            SettledCancelled(written);   // asked to cancel while it ran: no gate, no reschedule
            return;
        }
        Notify(rec.WorkType, JobEventKind.Rescheduled, null, written);

        if (gate)
        {
            var key = hasOverride ? (gateKeyOverride ?? rec.GateKey) : rec.GateKey;
            if (!string.IsNullOrEmpty(key))
                await _ledger.SetGate(key!, until, reason, CancellationToken.None);
        }
    }

    private async Task SettleCancelledAsync(JobRecord rec, string token)
    {
        var now = _clock.GetUtcNow();
        rec.Owner = null;
        rec.LeaseUntil = null;
        rec.ReservedFor = null;
        rec.ReservedUntil = null;
        rec.LastSettledAt = now;
        rec.ExpireAt = ExpiryAt(_options.ArchiveAfter, now);
        SetStatus(rec, JobStatus.Cancelled, now, "cancelled");
        if (await EndClaimAsync(rec, token) is not { } written) return;
        _metrics.Record(rec.WorkType, JobStatus.Cancelled, now);
        Notify(rec.WorkType, JobEventKind.Cancelled, null, written);
    }

    private async Task SettleShutdownAsync(JobRecord rec, string token)
    {
        var now = _clock.GetUtcNow();
        rec.Attempt = Math.Max(0, rec.Attempt - 1); // worker stopped before completing; don't penalize
        rec.Owner = null;
        rec.LeaseUntil = null;
        rec.ReservedFor = null;
        rec.ReservedUntil = null;
        rec.VisibleAt = now;
        SetStatus(rec, JobStatus.Queued, now, "requeued (worker shutdown)");
        if (await EndClaimAsync(rec, token) is { IsTerminal: true } cancelled) SettledCancelled(cancelled);
    }

    /// <summary>A write conflict means the handler's work was valid but made against a stale item (JOBS-0010). It never
    /// consumes an attempt and never advances a chain: the job runs again on the stored item after a short delay, up to
    /// <see cref="JobsOptions.MaxWriteConflicts"/> times, and past that bound it is dead-lettered as
    /// <see cref="DeadReason.Conflict"/>.</summary>
    private async Task SettleConflictAsync(JobRecord rec, WorkItemConflictException conflict, string token)
    {
        var now = _clock.GetUtcNow();
        rec.Attempt = Math.Max(0, rec.Attempt - 1);   // a conflict is not a failed attempt: undo the claim-time increment
        rec.Conflicts++;
        rec.Owner = null;
        rec.LeaseUntil = null;
        rec.ReservedFor = null;
        rec.ReservedUntil = null;
        rec.LastSettledAt = now;

        if (rec.Conflicts > _options.MaxWriteConflicts)
        {
            rec.LastError = $"{conflict.Message} The job met a conflicting write {rec.Conflicts} times, more than " +
                            $"JobsOptions.MaxWriteConflicts ({_options.MaxWriteConflicts}) allows, and stopped.";
            rec.DeadReason = DeadReason.Conflict.ToString();
            rec.ExpireAt = ExpiryAt(_options.FailedAfter, now);
            SetStatus(rec, JobStatus.Dead, now, $"write conflict {rec.Conflicts} exceeded {_options.MaxWriteConflicts}");
            if (await EndClaimAsync(rec, token) is not { } dead) return;
            _metrics.Record(rec.WorkType, JobStatus.Dead, now);
            Notify(rec.WorkType, JobEventKind.DeadLettered, null, dead, rec.LastError);
            return;
        }

        rec.LastError = $"{conflict.Message} It runs again on the stored item ({rec.Conflicts}/{_options.MaxWriteConflicts}).";
        rec.DeferReason = "write-conflict";
        rec.VisibleAt = now + RetryDelay(rec.Conflicts);
        SetStatus(rec, JobStatus.Queued, now, $"write conflict {rec.Conflicts}/{_options.MaxWriteConflicts}");
        if (await EndClaimAsync(rec, token) is not { } written) return;
        if (written.IsTerminal) SettledCancelled(written);
        else Notify(rec.WorkType, JobEventKind.Rescheduled, null, written);
    }

    /// <summary>Archival sweep (§19.3): purge Completed/Cancelled past <c>ArchiveAfter</c>, Failed/Dead past
    /// <c>FailedAfter</c>, then trim each work-type's terminal rows to <c>RetainPerWorkType</c>. Each is independently
    /// gated; all off → no-op. Returns the total rows removed.</summary>
    public async Task<int> ArchiveAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        var purged = 0;
        // JOBS-0010: every removal reads and removes one bounded page at a time.
        if (_options.ArchiveAfter > TimeSpan.Zero)
            purged += await InPages(limit => _ledger.PurgeArchivable(now - _options.ArchiveAfter, limit, ct));
        if (_options.FailedAfter > TimeSpan.Zero)
            purged += await InPages(limit => _ledger.PurgeFailed(now - _options.FailedAfter, limit, ct));
        if (_options.RetainPerWorkType > 0)
            foreach (var binding in _registry.All)
                purged += await InPages(limit => _ledger.TrimTerminal(binding.WorkType, _options.RetainPerWorkType, limit, ct));

        // JOBS-0010: a released gate has no further effect; its row goes, a bounded page at a time.
        purged += await InPages(limit => _ledger.PurgeReleasedGates(now, limit, ct));

        // §20.2 metrics rollup: bucket-age retention of the node-sharded JobMetric rows.
        if (_metrics.Enabled && _options.MetricsRetention > TimeSpan.Zero)
            purged += await _metrics.PurgeAsync(now - _options.MetricsRetention, ct);

        // §19.4 self-reporting guardrail: name the job-per-row anti-pattern when a work-type's active set is huge.
        if (_options.JobPerRowWarnThreshold > 0)
            foreach (var binding in _registry.All)
            {
                var active = await _ledger.CountActive(binding.WorkType, ct);
                if (active > _options.JobPerRowWarnThreshold)
                    _logger.LogWarning(
                        "[Koan.Jobs] WorkType '{WorkType}' has {Active:N0} active rows (> {Threshold:N0}) — this looks " +
                        "like job-per-row. Window the source with a cursor-conveyor (jobs-howto §8.1; JOBS-0005 §19.4).",
                        binding.WorkType, active, _options.JobPerRowWarnThreshold);
            }
        return purged;
    }

    /// <summary>Run a bounded removal until a page falls short of the page size; the sum is what it removed.</summary>
    private static async Task<int> InPages(Func<int, Task<int>> page)
    {
        var total = 0;
        int removed;
        do
        {
            removed = await page(Infrastructure.Constants.Retention.PageSize);
            total += removed;
        } while (removed >= Infrastructure.Constants.Retention.PageSize);
        return total;
    }

    /// <summary>Reclaim jobs whose lease lapsed (reaper sweep): revert Running → Queued for re-dispatch. The reaper runs
    /// beside the executions it may reclaim (JOBS-0010), so each reclaim is a compare on the revision the sweep read: a
    /// row its owner renewed, settled or that another node cancelled since keeps that write. Then held chain successors
    /// whose holder claim no longer exists are released or deleted.</summary>
    public async Task ReapAsync(CancellationToken ct = default)
    {
        using var storageScope = JobsStorageScope.Enter();
        var now = _clock.GetUtcNow();
        await ReclaimInPages(limit => _ledger.Stuck(now, limit, ct), now, "reclaimed (lease lapsed)", ct);

        // PMC-055: a worker confirmed dead by roster silence forfeits its running jobs immediately —
        // ahead of lease lapse. Safe because the revived node's renewal and settlement are
        // fenced on their claim token: it abandons without writing. This node never death-reclaims its own
        // in-flight jobs (its heartbeat governs them; the lease is the only bound).
        if (_options.ReclaimFromConfirmedDead && _options.WorkerDeathTimeout > TimeSpan.Zero)
        {
            var cutoff = now - _options.WorkerDeathTimeout;
            var alive = (await WorkerNode.Query(w => w.LastSeenAt >= cutoff, ct))
                .Select(w => w.Id)
                .ToHashSet(StringComparer.Ordinal);
            alive.Add(_owner);

            await ReclaimInPages(limit => _ledger.Orphaned(alive, limit, ct), now, "reclaimed (worker confirmed dead)", ct);

            foreach (var stale in await WorkerNode.Query(w => w.LastSeenAt < cutoff, ct))
                await stale.Remove(ct);
        }

        await CloseOrphanedSuccessorsAsync(now, ct);
    }

    /// <summary>Reclaim a sweep's rows a bounded page at a time. Each reclaim moves its row out of the sweep's set, so the
    /// next page is read from the start; a page in which nothing could be reclaimed ends the sweep.</summary>
    private async Task ReclaimInPages(Func<int, Task<IReadOnlyList<JobRecord>>> page, DateTimeOffset now, string note, CancellationToken ct)
    {
        while (true)
        {
            var rows = await page(Infrastructure.Constants.Retention.PageSize);
            var reclaimed = 0;
            foreach (var row in rows)
                if (await ReclaimAsync(row, now, note, ct)) reclaimed++;
            if (rows.Count < Infrastructure.Constants.Retention.PageSize || reclaimed == 0) return;
        }
    }

    /// <summary>End a lapsed claim (JOBS-0010): the row returns to the queue as a compare on the revision the sweep read,
    /// never read again — a renewal, a settlement or a cancellation landing since keeps the row, and the next sweep looks
    /// again. A job asked to cancel settles Cancelled instead. The reclaim drops the claim token and gives both slots
    /// back, and the successor the lapsed run appended, held for it, is deleted.</summary>
    private async Task<bool> ReclaimAsync(JobRecord row, DateTimeOffset now, string note, CancellationToken ct)
    {
        var token = row.ClaimToken;
        row.Owner = null;
        row.LeaseUntil = null;
        row.ReservedFor = null;
        row.ReservedUntil = null;
        row.ClaimToken = null;
        row.ReleaseRunSlot();
        var cancelled = row.CancelRequestedAt is not null;
        if (cancelled)
        {
            row.LastSettledAt = now;
            row.ExpireAt = ExpiryAt(_options.ArchiveAfter, now);
            SetStatus(row, JobStatus.Cancelled, now, $"cancelled (asked while it ran; {note})");
        }
        else
        {
            row.VisibleAt = now;
            SetStatus(row, JobStatus.Queued, now, note);
        }
        if (!await _ledger.Update(row, ct)) return false;   // changed since the sweep read it: the next sweep looks again
        if (token is not null) await _ledger.DeleteHeld(JobRecord.SuccessorId(row.Id, token), token, ct);
        if (cancelled)
        {
            _metrics.Record(row.WorkType, JobStatus.Cancelled, now);
            Notify(row.WorkType, JobEventKind.Cancelled, null, row);
        }
        else
        {
            Notify(row.WorkType, JobEventKind.Stalled, null, row);
        }
        return true;
    }

    /// <summary>A held chain successor whose holder claim no longer exists (JOBS-0010) is released only when the
    /// predecessor it waits for settled with the very status the successor was appended for
    /// (<see cref="JobRecord.HeldOn"/>: Completed on the success path, Failed under <c>OnFailure.Continue</c>), carries no
    /// cancellation request, and was settled by that very claim (<see cref="JobRecord.SettledBy"/> equals
    /// <see cref="JobRecord.HeldBy"/>); otherwise it is deleted. A run that stalled, was reclaimed and whose next run settled
    /// the job thus never releases a second successor, and a stage recorded Failed under <c>OnFailure.Abort</c> never
    /// releases the successor its success path appended.</summary>
    private async Task CloseOrphanedSuccessorsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var size = Infrastructure.Constants.Retention.PageSize;
        for (var page = 1; ; page++)
        {
            var held = await _ledger.Held(page, size, ct);
            var closed = 0;
            foreach (var successor in held)
            {
                if (successor.HeldBy is not { } holder) continue;
                var predecessor = successor.HeldFor is { } id ? await _ledger.Get(id, ct) : null;
                if (predecessor is not null && string.Equals(predecessor.ClaimToken, holder, StringComparison.Ordinal))
                    continue;   // its holder still runs
                var settledByHolder = predecessor is { CancelRequestedAt: null } && predecessor.Status == successor.HeldOn
                                      && string.Equals(predecessor.SettledBy, holder, StringComparison.Ordinal);
                var done = settledByHolder
                    ? await _ledger.ReleaseHeld(successor.Id, holder, cancel: false, now, ct)
                    : await _ledger.DeleteHeld(successor.Id, holder, ct);
                if (done) closed++;
            }
            if (held.Count < size) return;
            if (closed > 0) page--;   // closed rows left the held set: read the same page again
        }
    }

    /// <summary>PMC-055: register this worker in the fleet roster, or heartbeat its existing entry. The worker calls it
    /// on its own <c>WorkerHeartbeatInterval</c> timer, independent of the work it runs (JOBS-0010).</summary>
    public async Task BeatAsync(CancellationToken ct = default)
    {
        using var storageScope = JobsStorageScope.Enter();
        var now = _clock.GetUtcNow();
        var node = await WorkerNode.Get(_owner, ct);
        if (node is null)
        {
            await WorkerNode.Upsert(new WorkerNode { Id = _owner, StartedAt = now, LastSeenAt = now, Machine = Environment.MachineName }, ct);
            return;
        }

        node.LastSeenAt = now;
        node.Machine = Environment.MachineName;
        await WorkerNode.Upsert(node, ct);
    }

    /// <summary>PMC-055: graceful departure — peers see the resignation immediately instead of
    /// waiting out the death timeout on a planned shutdown.</summary>
    public async Task ResignAsync(CancellationToken ct = default)
    {
        using var storageScope = JobsStorageScope.Enter();
        var node = await WorkerNode.Get(_owner, ct);
        if (node is not null)
            await node.Remove(ct);
    }

    /// <summary>PMC-056 reservation dispatch ("the jar hands a cookie to a named hand"). In
    /// <see cref="JobDispatchMode.Reservation"/> only, the senior live roster node — derived from the shared roster,
    /// no election protocol — releases reservations whose hand died or whose stamp lapsed, then assigns the oldest
    /// due unreserved work onto the least-loaded live hands, bounded by fleet concurrency. Self-throttled to
    /// <c>WorkerHeartbeatInterval</c>; a no-op in <see cref="JobDispatchMode.Pull"/>.
    ///
    /// <para>Safety: assignment is ledger-verifiable dispatch metadata on the row itself (<see cref="JobRecord.ReservedFor"/>);
    /// stamps are guarded writes that apply only while the row is Queued and unreserved; claims still CAS and respect a
    /// fresh foreign reservation; releases re-verify the stored row immediately before writing. A lost release race is the
    /// same at-least-once window the optimistic claim floor already carries. No transition rows and no events: assignment is
    /// not lifecycle.</para></summary>
    public async Task AssignReservationsAsync(CancellationToken ct = default)
    {
        using var storageScope = JobsStorageScope.Enter();
        if (_options.DispatchMode != JobDispatchMode.Reservation) return;
        if (_options.WorkerDeathTimeout <= TimeSpan.Zero) return;

        var now = _clock.GetUtcNow();
        if (now - _lastAssignAt < _options.WorkerHeartbeatInterval) return;
        _lastAssignAt = now;

        // The hands: fresh roster entries plus me (my heartbeat may be pending; I am alive by definition).
        var cutoff = now - _options.WorkerDeathTimeout;
        var alive = (await WorkerNode.Query(w => w.LastSeenAt >= cutoff, ct))
            .Select(w => w.Id)
            .Append(_owner)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var aliveSet = alive.ToHashSet(StringComparer.Ordinal);

        // Seniority before action: exactly one node coordinates per pass. Oldest join wins; ties break by owner id.
        // Concurrent-seniority windows (between heartbeats) are absorbed by the guarded TryReserve stamps.
        var nodes = await WorkerNode.Query(w => w.LastSeenAt >= cutoff, ct);
        var seniorId = nodes
            .OrderBy(n => n.StartedAt)
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .Select(n => n.Id)
            .DefaultIfEmpty(_owner)     // empty roster → I coordinate (single-node host or pending beats)
            .First();
        if (!string.Equals(seniorId, _owner, StringComparison.Ordinal)) return;

        // 1. Release: lapsed stamps return to the shelf; so do confirmed-dead hands' assignments.
        foreach (var reserved in await _ledger.Reservations(ct))
        {
            var handLive = reserved.ReservedFor is { } hand && aliveSet.Contains(hand);
            var lapsed = reserved.ReservedUntil is { } until && until < now;
            if (handLive && !lapsed) continue;

            // Re-verify against the stored row so we never overwrite a claim that landed since the read.
            var stored = await _ledger.Get(reserved.Id!, ct);
            if (stored is not { Status: JobStatus.Queued }
                || !string.Equals(stored.ReservedFor, reserved.ReservedFor, StringComparison.Ordinal)) continue;
            _logger.LogDebug("Koan.Jobs released job {JobId} from reservation for {Hand} ({Reason}).",
                stored.Id, stored.ReservedFor, lapsed ? "reservation lapsed" : "hand confirmed dead");
            stored.ReservedFor = null;
            stored.ReservedUntil = null;
            await _ledger.Update(stored, ct);   // a claim that landed since the read wins, and nothing is written
        }

        // 2. Assign: oldest due first, least-loaded hand wins ties by roster order. Outstanding cookies cap at
        //    fleet capacity (hands × WorkerConcurrency) and one scan batch per pass.
        var load = new Dictionary<string, int>(await _ledger.RunningLoad(alive, ct), StringComparer.Ordinal);
        foreach (var res in await _ledger.Reservations(ct))
            if (res.ReservedFor is { } hand && aliveSet.Contains(hand)) load[hand] = load.GetValueOrDefault(hand) + 1;

        var capacity = alive.Sum(h => Math.Max(0, _options.WorkerConcurrency - load.GetValueOrDefault(h)));
        if (capacity <= 0) return;
        var limit = Math.Min(capacity, Math.Max(1, _options.ClaimScanBatch));
        foreach (var candidate in await _ledger.ReservationCandidates(now, limit, ct))
        {
            if (candidate.Id is null) continue;
            var hand = alive
                .Where(h => load.GetValueOrDefault(h) < _options.WorkerConcurrency)
                .OrderBy(h => load.GetValueOrDefault(h))
                .ThenBy(h => h, StringComparer.Ordinal)
                .FirstOrDefault();
            if (hand is null) break;   // every hand is at capacity
            if (await _ledger.TryReserve(candidate.Id, hand, now + _options.ReservationDuration, now, ct))
                load[hand] = load.GetValueOrDefault(hand) + 1;
        }
    }

    // --- helpers ---

    /// <summary>
    /// Claims the next executable row. Durable rows can outlive the application code that registered their work type;
    /// those rows are deterministic poison, so the orchestrator terminalizes them and keeps looking instead of leaking
    /// a Running lease or making a caller mistake a retired row for an empty queue.
    /// </summary>
    private async Task<(JobRecord Record, JobTypeBinding Binding)?> ClaimBoundNextAsync(
        DateTimeOffset now,
        IReadOnlyCollection<string> saturatedLanes,
        IReadOnlyDictionary<string, PoolDispatchContext>? pools,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var rec = await _ledger.ClaimNext(
                _owner,
                now,
                now + _options.LeaseDuration,
                saturatedLanes,
                ct,
                pools);
            if (rec is null) return null;

            var binding = _registry.Get(rec.WorkType);
            if (binding is not null) return (rec, binding);

            await SettleUnregisteredWorkTypeAsync(rec, rec.ClaimToken!);
        }

        return null;
    }

    private async Task<IReadOnlyDictionary<string, PoolDispatchContext>?> ResolvePoolContextsAsync(CancellationToken ct)
    {
        if (_poolResolvers.Count == 0) return null;
        var dict = new Dictionary<string, PoolDispatchContext>(_poolResolvers.Count, StringComparer.Ordinal);
        foreach (var resolver in _poolResolvers)
        {
            var members = await resolver.GetMembersAsync(ct);
            dict[resolver.PoolName] = new PoolDispatchContext(resolver.PoolName, members, resolver.CapacityPerMember);
        }
        return dict;
    }

    private IReadOnlyCollection<string> SaturatedLanes()
    {
        List<string>? saturated = null;
        foreach (var kv in _lanes)
            if (kv.Value.CurrentCount == 0)
                (saturated ??= new()).Add(kv.Key);
        return (IReadOnlyCollection<string>?)saturated ?? Array.Empty<string>();
    }

    private SemaphoreSlim LaneSem(string lane, int maxConcurrency)
        => _lanes.GetOrAdd(lane, _ => new SemaphoreSlim(maxConcurrency, maxConcurrency));

    private async Task<bool> IsCancelMarkerSet(string jobId)
        => (await _ledger.Get(jobId, CancellationToken.None))?.CancelRequestedAt is not null;

    private DateTimeOffset ApplyJitter(DateTimeOffset releaseAt)
    {
        if (_options.RescheduleJitter <= TimeSpan.Zero) return releaseAt;
        var ms = Random.Shared.Next(0, (int)Math.Max(1, _options.RescheduleJitter.TotalMilliseconds));
        return releaseAt + TimeSpan.FromMilliseconds(ms);
    }

    private TimeSpan RetryDelay(int attempt)
    {
        var factor = Math.Pow(2, Math.Min(attempt - 1, 16));
        var ticks = (long)Math.Min(_options.RetryBaseDelay.Ticks * factor, TimeSpan.FromMinutes(5).Ticks);
        return TimeSpan.FromTicks(Math.Max(ticks, 0));
    }

    private static JobState ToState(JobRecord r) => new(
        r.Status, r.Action, r.Attempt, r.Reschedules, r.FirstSubmittedAt, r.LastSettledAt,
        r.LastError, r.DeferReason, r.Deadline, r.CorrelationId)
    {
        GateKey = r.GateKey,   // the gate captured with THIS claim: declared/resolved key, or the pool-elected member
    };

    private static void SetStatus(JobRecord r, JobStatus to, DateTimeOffset at, string? note)
    {
        r.Transitions.Add(new JobTransition { At = at, From = r.Status, To = to, Note = note });
        r.Status = to;
    }

    /// <summary>Absolute expiry for a terminal row (§20.4): <c>now + window</c>, or null when the window is disabled
    /// (≤ 0) so the row is retained indefinitely.</summary>
    private static DateTimeOffset? ExpiryAt(TimeSpan window, DateTimeOffset now) => window > TimeSpan.Zero ? now + window : null;
}
