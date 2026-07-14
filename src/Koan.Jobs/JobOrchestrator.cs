using System.Collections.Concurrent;
using System.Text.Json;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Jobs;

/// <summary>
/// The one concern that claims, executes, settles, recalls, cancels, and advances jobs (JOBS-0005 §7). It talks only
/// to <see cref="IJobLedger"/> (storage-agnostic) and the <see cref="JobTypeRegistry"/> (bound handlers). Deterministic
/// by design: <see cref="DrainAsync"/> processes all currently-ready work to completion (the test driver); the worker
/// service drives the same loop continuously in production. All time comes from an injected <see cref="TimeProvider"/>.
/// </summary>
public sealed class JobOrchestrator
{
    private readonly IJobLedger _ledger;
    private readonly JobTypeRegistry _registry;
    private readonly JobsOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<JobOrchestrator> _logger;
    private readonly IServiceScopeFactory _scopes;
    private readonly IReadOnlyList<IJobPoolResolver> _poolResolvers;
    private readonly AmbientCarrierRegistry _carrier;

    private readonly string _owner = Guid.CreateVersion7().ToString("N");
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _lanes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new(StringComparer.Ordinal);
    private readonly JobMetricsRecorder _metrics;

    public JobOrchestrator(
        IJobLedger ledger, JobTypeRegistry registry, IOptions<JobsOptions> options,
        TimeProvider clock, ILogger<JobOrchestrator> logger, IServiceScopeFactory scopes,
        IEnumerable<IJobPoolResolver> poolResolvers, AmbientCarrierRegistry carrier)
    {
        _ledger = ledger;
        _registry = registry;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
        _scopes = scopes;
        _poolResolvers = poolResolvers.ToList();
        _carrier = carrier;
        _metrics = new JobMetricsRecorder(_options.MetricsEnabled, _owner, _clock);
    }

    /// <summary>Fold this node's accumulated throughput deltas into its <see cref="JobMetric"/> shard rows (§20.2).
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
                // Global cap: don't claim more work than this node can run concurrently.
                if (_options.WorkerConcurrency > 0 && inflight.Count >= _options.WorkerConcurrency)
                {
                    await Task.WhenAny(inflight);
                    inflight.RemoveAll(t => t.IsCompleted);
                    continue;
                }

                var now = _clock.GetUtcNow();
                var pools = await ResolvePoolContextsAsync(ct);
                var rec = await _ledger.ClaimNext(_owner, now, now + _options.LeaseDuration, SaturatedLanes(), ct, pools);
                if (rec is not null)
                {
                    var binding = _registry.Require(rec.WorkType);
                    var policy = binding.ResolvePolicy(rec.Action, _options);
                    var sem = LaneSem(policy.Lane, policy.MaxConcurrency);
                    if (!sem.Wait(0)) await sem.WaitAsync(ct); // claim guarantees a slot; fallback is defensive
                    inflight.Add(ExecuteAndReleaseAsync(rec, binding, policy, sem, ct));
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

    /// <summary>Fire the cancellation token of a job currently running on this node (the durable marker is set by the caller).</summary>
    public void SignalCancel(string jobId)
    {
        if (_running.TryGetValue(jobId, out var cts))
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

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
        var now = _clock.GetUtcNow();
        var pools = await ResolvePoolContextsAsync(ct);
        var rec = await _ledger.ClaimNext(_owner, now, now + _options.LeaseDuration, SaturatedLanes(), ct, pools);
        if (rec is null) return null;

        var binding = _registry.Require(rec.WorkType);
        var policy = binding.ResolvePolicy(rec.Action, _options);
        var sem = LaneSem(policy.Lane, policy.MaxConcurrency);
        if (!sem.Wait(0)) await sem.WaitAsync(ct);
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
        // ARCH-0100: rehydrate the ambient slices captured at submit BEFORE loading the (possibly tenant-scoped)
        // work-item, and keep them in scope across load + execute + settle (the conditional auto-save included) so
        // every tenant-scoped read/write runs in the submitted tenant. A restore failure (an unregistered axis, or
        // an unknown carrier format) is deterministic and non-retryable → dead-letter; the handler never runs
        // fail-open in a wrong/absent ambient. A null/empty bag restores nothing (the §1b request guard owns the
        // unscoped-write refusal under Closed; dev-fallback under Open).
        IDisposable ambientScope;
        try { ambientScope = _carrier.Restore(rec.AmbientCarrier); }
        catch (Exception ex) { await SettleCarrierFailureAsync(rec, ex); return null; }
        using var _ambient = ambientScope;

        object? workItem;
        try { workItem = await binding.Load(rec.WorkId, workerCt); }
        catch (Exception ex) { await SettleFailureAsync(rec, binding, policy, ex); return null; }
        // Type-level triggers (TriggerAsync) use an ephemeral singleton that is never persisted; re-create it here.
        workItem ??= rec.WorkId == JobCoordinator.SingletonWorkId ? binding.NewSingleton(rec.WorkId) : null;
        if (workItem is null) { await SettleFailureAsync(rec, binding, policy, new InvalidOperationException($"Work-item {rec.WorkType}/{rec.WorkId} not found.")); return null; }

        var snapshot = Snapshot(workItem);   // for conditional auto-save (§17.1): only persist if the handler mutates it

        using var timeoutCts = policy.Timeout is { } to
            ? new CancellationTokenSource(to, _clock)
            : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(workerCt, timeoutCts.Token);
        _running[rec.Id] = linked;
        if (rec.CancelRequestedAt is not null) { try { linked.Cancel(); } catch (ObjectDisposedException) { } }

        using var scope = _scopes.CreateScope();
        Task PersistProgress(string jobId, double fraction, string? message, CancellationToken ct)
        {
            // Progress is written immediately for live observers. Keep the claimed snapshot in sync too, otherwise
            // the subsequent settle Update would overwrite that newer ledger row with its pre-handler values.
            rec.ProgressFraction = fraction;
            rec.ProgressMessage = message;
            return ProgressSink(jobId, fraction, message, ct);
        }

        var ctx = new JobContext(rec.Action, rec.Id, scope.ServiceProvider, _logger, ToState(rec), _clock, linked.Token, PersistProgress);
        try
        {
            await binding.Execute(workItem, ctx, linked.Token);
            await SettleSuccessAsync(rec, workItem, binding, policy, ctx, snapshot);
        }
        catch (RescheduleException rex)
        {
            var until = rex.Until ?? _clock.GetUtcNow() + (rex.After ?? TimeSpan.Zero);
            await ApplyDeferralAsync(rec, binding, policy, until, rex.Gate, rex.Gate ? rex.GateKey : null, hasOverride: rex.Gate, "reschedule-exception");
        }
        catch (OperationCanceledException)
        {
            if (timeoutCts.IsCancellationRequested)
                await SettleFailureAsync(rec, binding, policy, new TimeoutException($"Action '{rec.Action}' exceeded its timeout."));
            else if (await IsCancelMarkerSet(rec.Id))
                await SettleCancelledAsync(rec);
            else
                await SettleShutdownAsync(rec); // worker stopping — revert for reclaim
        }
        catch (Exception ex)
        {
            await SettleFailureAsync(rec, binding, policy, ex);
        }
        finally
        {
            _running.TryRemove(rec.Id, out _);
        }
        return ctx;
    }

    // --- settle paths ---

    private async Task SettleSuccessAsync(JobRecord rec, object workItem, JobTypeBinding binding, ResolvedActionPolicy policy, JobContext ctx, string? snapshot)
    {
        if (ctx.Signal is JobSignal.Reschedule or JobSignal.Backoff)
        {
            var until = ctx.DeferUntil ?? _clock.GetUtcNow();
            await ApplyDeferralAsync(rec, binding, policy, until, ctx.Signal == JobSignal.Backoff, ctx.GateKeyOverride, ctx.GateKeyOverrideSet, "cooperative-backoff");
            return;
        }

        // Conditional auto-save (§17.1): persist the work-item only if the handler mutated the loaded reference.
        // A handler that worked on its own copy (and saved it) left this one clean — don't clobber its write.
        if (snapshot is null || Snapshot(workItem) != snapshot)
            await binding.Save(workItem, CancellationToken.None);

        var now = _clock.GetUtcNow();
        var next = ctx.Signal switch
        {
            JobSignal.ContinueWith => ctx.NextAction,
            JobSignal.StopChain => null,
            _ => binding.NextInChain(rec.Action),
        };

        // Settle-window cancel check: CancelWorkAsync writes CancelRequestedAt to the durable ledger while the
        // handler runs, but the orchestrator's rec clone was loaded at claim time (before that write). Re-read
        // BEFORE overwriting the record with Completed — otherwise our Update below erases the marker and the
        // subsequent check sees a clean record every time.
        var cancelledInSettleWindow = next is not null && await IsCancelMarkerSet(rec.Id);

        rec.Owner = null;
        rec.LeaseUntil = null;
        rec.LastError = null;
        rec.DeferReason = null;
        rec.LastSettledAt = now;
        rec.ExpireAt = ExpiryAt(_options.ArchiveAfter, now);
        SetStatus(rec, JobStatus.Completed, now, "completed");
        await _ledger.Update(rec, CancellationToken.None);
        _metrics.Record(rec.WorkType, JobStatus.Completed, now);

        if (next is not null && !cancelledInSettleWindow)
        {
            var nextPolicy = binding.ResolvePolicy(next, _options);
            // Chain stages inherit the gate key resolved at submit (the chain's gate pool is fixed — §18) and the
            // ambient carrier (ARCH-0100 §7): the successor is appended here by the orchestrator, NOT the
            // coordinator, so capture-at-submit never fires for it — propagate the parent's bag verbatim.
            var nextRec = JobRecordFactory.Create(binding, nextPolicy, workItem, rec.WorkId, next, now, null, rec.CorrelationId, rec.GateKey, rec.AmbientCarrier);
            await _ledger.Append(nextRec, CancellationToken.None);
        }
    }

    private async Task SettleFailureAsync(JobRecord rec, JobTypeBinding binding, ResolvedActionPolicy policy, Exception ex)
    {
        var now = _clock.GetUtcNow();
        rec.Owner = null;
        rec.LeaseUntil = null;
        rec.LastError = ex.Message;
        rec.LastSettledAt = now;

        if (rec.Attempt < policy.MaxAttempts)
        {
            rec.VisibleAt = now + RetryDelay(rec.Attempt);
            SetStatus(rec, JobStatus.Queued, now, $"retry {rec.Attempt}/{policy.MaxAttempts}: {ex.GetType().Name}");
            await _ledger.Update(rec, CancellationToken.None);
            return;
        }

        rec.DeadReason = DeadReason.Poison.ToString();
        rec.ExpireAt = ExpiryAt(_options.FailedAfter, now);
        SetStatus(rec, JobStatus.Failed, now, $"failed after {policy.MaxAttempts} attempts: {ex.Message}");
        await _ledger.Update(rec, CancellationToken.None);
        _metrics.Record(rec.WorkType, JobStatus.Failed, now);

        if (policy.OnFailure == OnFailure.Continue && binding.NextInChain(rec.Action) is { } next)
        {
            var wi = await binding.Load(rec.WorkId, CancellationToken.None);
            if (wi is not null)
            {
                var nextPolicy = binding.ResolvePolicy(next, _options);
                await _ledger.Append(JobRecordFactory.Create(binding, nextPolicy, wi, rec.WorkId, next, now, null, rec.CorrelationId, rec.GateKey, rec.AmbientCarrier), CancellationToken.None);
            }
        }
    }

    /// <summary>ARCH-0100: the captured ambient could not be rehydrated (an unregistered axis, or an unknown
    /// carrier format). Deterministic — retrying would fail identically — so dead-letter immediately rather than
    /// run the handler in a wrong/absent ambient (fail-closed). The work-item is never loaded.</summary>
    private async Task SettleCarrierFailureAsync(JobRecord rec, Exception ex)
    {
        var now = _clock.GetUtcNow();
        rec.Owner = null;
        rec.LeaseUntil = null;
        rec.LastError = ex.Message;
        rec.LastSettledAt = now;
        rec.DeadReason = DeadReason.CarrierRestoreFailed.ToString();
        rec.ExpireAt = ExpiryAt(_options.FailedAfter, now);
        SetStatus(rec, JobStatus.Dead, now, $"ambient carrier restore failed: {ex.Message}");
        await _ledger.Update(rec, CancellationToken.None);
        _metrics.Record(rec.WorkType, JobStatus.Dead, now);
    }

    private async Task ApplyDeferralAsync(JobRecord rec, JobTypeBinding binding, ResolvedActionPolicy policy,
        DateTimeOffset until, bool gate, string? gateKeyOverride, bool hasOverride, string reason)
    {
        var now = _clock.GetUtcNow();
        // reschedule does NOT consume a retry attempt: undo the claim-time increment.
        rec.Attempt = Math.Max(0, rec.Attempt - 1);
        rec.Reschedules++;

        var deadlineHit = rec.Deadline is { } dl && now >= dl;
        var maxHit = policy.MaxReschedules >= 0 && rec.Reschedules > policy.MaxReschedules;
        rec.Owner = null;
        rec.LeaseUntil = null;
        rec.LastSettledAt = now;

        if (deadlineHit || maxHit)
        {
            rec.DeadReason = DeadReason.PerpetuallyDeferred.ToString();
            rec.ExpireAt = ExpiryAt(_options.FailedAfter, now);
            SetStatus(rec, JobStatus.Dead, now, deadlineHit ? "deadline exceeded" : "max reschedules exceeded");
            await _ledger.Update(rec, CancellationToken.None);
            _metrics.Record(rec.WorkType, JobStatus.Dead, now);
            return;
        }

        rec.DeferReason = reason;
        rec.VisibleAt = ApplyJitter(until);
        SetStatus(rec, JobStatus.Queued, now, $"deferred to {rec.VisibleAt:O} ({reason})");
        await _ledger.Update(rec, CancellationToken.None);

        if (gate)
        {
            var key = hasOverride ? (gateKeyOverride ?? rec.GateKey) : rec.GateKey;
            if (!string.IsNullOrEmpty(key))
                await _ledger.SetGate(key!, until, reason, CancellationToken.None);
        }
    }

    private async Task SettleCancelledAsync(JobRecord rec)
    {
        var now = _clock.GetUtcNow();
        rec.Owner = null;
        rec.LeaseUntil = null;
        rec.LastSettledAt = now;
        rec.ExpireAt = ExpiryAt(_options.ArchiveAfter, now);
        SetStatus(rec, JobStatus.Cancelled, now, "cancelled");
        await _ledger.Update(rec, CancellationToken.None);
        _metrics.Record(rec.WorkType, JobStatus.Cancelled, now);
    }

    private async Task SettleShutdownAsync(JobRecord rec)
    {
        var now = _clock.GetUtcNow();
        rec.Attempt = Math.Max(0, rec.Attempt - 1); // worker stopped before completing; don't penalize
        rec.Owner = null;
        rec.LeaseUntil = null;
        rec.VisibleAt = now;
        SetStatus(rec, JobStatus.Queued, now, "requeued (worker shutdown)");
        await _ledger.Update(rec, CancellationToken.None);
    }

    /// <summary>Archival sweep (§19.3): purge Completed/Cancelled past <c>ArchiveAfter</c>, Failed/Dead past
    /// <c>FailedAfter</c>, then trim each work-type's terminal rows to <c>RetainPerWorkType</c>. Each is independently
    /// gated; all off → no-op. Returns the total rows removed.</summary>
    public async Task<int> ArchiveAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        var purged = 0;
        if (_options.ArchiveAfter > TimeSpan.Zero)
            purged += await _ledger.PurgeArchivable(now - _options.ArchiveAfter, ct);
        if (_options.FailedAfter > TimeSpan.Zero)
            purged += await _ledger.PurgeFailed(now - _options.FailedAfter, ct);
        if (_options.RetainPerWorkType > 0)
            foreach (var binding in _registry.All)
                purged += await _ledger.TrimTerminal(binding.WorkType, _options.RetainPerWorkType, ct);

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
                        "like job-per-row. Window the source with a cursor-conveyor (jobs-howto §bulk; JOBS-0005 §19.4).",
                        binding.WorkType, active, _options.JobPerRowWarnThreshold);
            }
        return purged;
    }

    /// <summary>Reclaim jobs whose lease lapsed (reaper sweep): revert Running → Queued for re-dispatch.</summary>
    public async Task ReapAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        foreach (var stuck in await _ledger.Stuck(now, ct))
        {
            stuck.Owner = null;
            stuck.LeaseUntil = null;
            stuck.VisibleAt = now;
            SetStatus(stuck, JobStatus.Queued, now, "reclaimed (lease lapsed)");
            await _ledger.Update(stuck, ct);
        }
    }

    // --- helpers ---

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

    private Task ProgressSink(string jobId, double fraction, string? message, CancellationToken ct)
        => _ledger.Progress(jobId, fraction, message, ct);

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
        r.LastError, r.DeferReason, r.Deadline, r.CorrelationId);

    private static void SetStatus(JobRecord r, JobStatus to, DateTimeOffset at, string? note)
    {
        r.Transitions.Add(new JobTransition { At = at, From = r.Status, To = to, Note = note });
        r.Status = to;
    }

    /// <summary>Absolute expiry for a terminal row (§20.4): <c>now + window</c>, or null when the window is disabled
    /// (≤ 0) so the row is retained indefinitely.</summary>
    private static DateTimeOffset? ExpiryAt(TimeSpan window, DateTimeOffset now) => window > TimeSpan.Zero ? now + window : null;

    /// <summary>Deterministic snapshot of a work-item's serialized state for conditional auto-save (§17.1). The
    /// comparison is internal (load vs. settle, same serializer), so it needs only determinism + public-state
    /// coverage. Returns null when the entity can't be serialized (cyclic/exotic) → the caller degrades to
    /// always-save, never failing the job over a snapshot.</summary>
    private static string? Snapshot(object workItem)
    {
        try { return JsonSerializer.Serialize(workItem, workItem.GetType()); }
        catch { return null; }
    }
}
