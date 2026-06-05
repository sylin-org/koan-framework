using System.Collections.Concurrent;
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

    private readonly string _owner = Guid.CreateVersion7().ToString("N");
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _lanes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new(StringComparer.Ordinal);

    public JobOrchestrator(
        IJobLedger ledger, JobTypeRegistry registry, IOptions<JobsOptions> options,
        TimeProvider clock, ILogger<JobOrchestrator> logger, IServiceScopeFactory scopes)
    {
        _ledger = ledger;
        _registry = registry;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
        _scopes = scopes;
    }

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
                var now = _clock.GetUtcNow();
                var rec = await _ledger.ClaimNext(_owner, now, now + _options.LeaseDuration, SaturatedLanes(), ct);
                if (rec is not null)
                {
                    var binding = _registry.Require(rec.WorkType);
                    var policy = binding.ResolvePolicy(rec.Action, _options);
                    var sem = LaneSem(policy.Lane, policy.MaxConcurrency);
                    if (!sem.Wait(0)) await sem.WaitAsync(ct); // claim guarantees a slot; fallback is defensive
                    inflight.Add(ExecuteAndReleaseAsync(rec, binding, policy, sem, ct));
                    continue;
                }

                inflight.RemoveAll(t => t.IsCompleted);
                if (inflight.Count == 0) break;
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

    private async Task ExecuteClaimedAsync(JobRecord rec, JobTypeBinding binding, ResolvedActionPolicy policy, CancellationToken workerCt)
    {
        object? workItem;
        try { workItem = await binding.Load(rec.WorkId, workerCt); }
        catch (Exception ex) { await SettleFailureAsync(rec, binding, policy, ex); return; }
        if (workItem is null) { await SettleFailureAsync(rec, binding, policy, new InvalidOperationException($"Work-item {rec.WorkType}/{rec.WorkId} not found.")); return; }

        using var timeoutCts = policy.Timeout is { } to
            ? new CancellationTokenSource(to, _clock)
            : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(workerCt, timeoutCts.Token);
        _running[rec.Id] = linked;
        if (rec.CancelRequestedAt is not null) { try { linked.Cancel(); } catch (ObjectDisposedException) { } }

        using var scope = _scopes.CreateScope();
        var ctx = new JobContext(rec.Action, rec.Id, scope.ServiceProvider, _logger, ToState(rec), _clock, linked.Token, ProgressSink);
        try
        {
            await binding.Execute(workItem, ctx, linked.Token);
            await SettleSuccessAsync(rec, workItem, binding, policy, ctx);
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
    }

    // --- settle paths ---

    private async Task SettleSuccessAsync(JobRecord rec, object workItem, JobTypeBinding binding, ResolvedActionPolicy policy, JobContext ctx)
    {
        if (ctx.Signal is JobSignal.Reschedule or JobSignal.Backoff)
        {
            var until = ctx.DeferUntil ?? _clock.GetUtcNow();
            await ApplyDeferralAsync(rec, binding, policy, until, ctx.Signal == JobSignal.Backoff, ctx.GateKeyOverride, ctx.GateKeyOverrideSet, "cooperative-backoff");
            return;
        }

        await binding.Save(workItem, CancellationToken.None);

        var now = _clock.GetUtcNow();
        var next = ctx.Signal switch
        {
            JobSignal.ContinueWith => ctx.NextAction,
            JobSignal.StopChain => null,
            _ => binding.NextInChain(rec.Action),
        };

        rec.Owner = null;
        rec.LeaseUntil = null;
        rec.LastError = null;
        rec.DeferReason = null;
        rec.LastSettledAt = now;
        SetStatus(rec, JobStatus.Completed, now, "completed");
        await _ledger.Update(rec, CancellationToken.None);

        if (next is not null)
        {
            var nextPolicy = binding.ResolvePolicy(next, _options);
            var nextRec = JobRecordFactory.Create(binding, nextPolicy, workItem, rec.WorkId, next, now, null, rec.CorrelationId);
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
        SetStatus(rec, JobStatus.Failed, now, $"failed after {policy.MaxAttempts} attempts: {ex.Message}");
        await _ledger.Update(rec, CancellationToken.None);

        if (policy.OnFailure == OnFailure.Continue && binding.NextInChain(rec.Action) is { } next)
        {
            var wi = await binding.Load(rec.WorkId, CancellationToken.None);
            if (wi is not null)
            {
                var nextPolicy = binding.ResolvePolicy(next, _options);
                await _ledger.Append(JobRecordFactory.Create(binding, nextPolicy, wi, rec.WorkId, next, now, null, rec.CorrelationId), CancellationToken.None);
            }
        }
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
            SetStatus(rec, JobStatus.Dead, now, deadlineHit ? "deadline exceeded" : "max reschedules exceeded");
            await _ledger.Update(rec, CancellationToken.None);
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
        SetStatus(rec, JobStatus.Cancelled, now, "cancelled");
        await _ledger.Update(rec, CancellationToken.None);
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

    /// <summary>Archival sweep: purge benign terminal rows (Completed/Cancelled) past <c>ArchiveAfter</c>.</summary>
    public Task<int> ArchiveAsync(CancellationToken ct = default)
    {
        if (_options.ArchiveAfter <= TimeSpan.Zero) return Task.FromResult(0);
        return _ledger.PurgeArchivable(_clock.GetUtcNow() - _options.ArchiveAfter, ct);
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
}
