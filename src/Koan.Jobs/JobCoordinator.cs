using System.Diagnostics;
using System.Text.Json;
using Koan.Core.Context;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Jobs;

/// <inheritdoc/>
public sealed class JobCoordinator : IJobCoordinator
{
    /// <summary>Stable id used by type-level triggers and scheduled ticks. Not persisted to the consumer's entity
    /// collection — the singleton is created in-memory at execution time by <see cref="JobTypeBinding.NewSingleton"/>.</summary>
    public const string SingletonWorkId = "__koan_job_singleton__";

    private readonly IJobLedger _ledger;
    private readonly JobTypeRegistry _registry;
    private readonly JobOrchestrator _orchestrator;
    private readonly IJobTransport _transport;
    private readonly IServiceProvider _services;
    private readonly JobsOptions _options;
    private readonly TimeProvider _clock;
    private readonly KoanContextCarrierRegistry _contextCarriers;

    /// <summary>Compatibility constructor for the public 0.17.0 infrastructure shape.</summary>
    [Obsolete("Direct JobCoordinator construction is compatibility-only; let AddKoan compose the Core context registry.")]
    public JobCoordinator(IJobLedger ledger, JobTypeRegistry registry, JobOrchestrator orchestrator,
        IJobTransport transport, IServiceProvider services, IOptions<JobsOptions> options, TimeProvider clock)
        : this(
            ledger,
            registry,
            orchestrator,
            transport,
            services,
            options,
            clock,
            services.GetService<KoanContextCarrierRegistry>() ?? new KoanContextCarrierRegistry([]))
    {
    }

    public JobCoordinator(IJobLedger ledger, JobTypeRegistry registry, JobOrchestrator orchestrator,
        IJobTransport transport, IServiceProvider services, IOptions<JobsOptions> options, TimeProvider clock,
        KoanContextCarrierRegistry contextCarriers)
    {
        _ledger = ledger;
        _registry = registry;
        _orchestrator = orchestrator;
        _transport = transport;
        _services = services;
        _options = options.Value;
        _clock = clock;
        _contextCarriers = contextCarriers;
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
        var binding = _registry.Require(workItem.GetType().FullName!);
        var policy = binding.ResolvePolicy(action, _options);
        var workId = binding.GetId(workItem);
        var now = _clock.GetUtcNow();
        var carrier = Carrier();   // capture the ambient ONCE here, before any await / scope change (ARCH-0100 §5)

        // Coalesce check BEFORE any entity save: a duplicate submit returns the existing handle without touching the
        // store. The key folds in the captured ambient so two tenants' idempotent submits never collide.
        var coalesceKey = JobCoalesce.FoldAmbient(binding.CoalesceKey(workItem, action), carrier);
        if (coalesceKey is not null)
        {
            var existing = await _ledger.FindActiveByCoalesceKey(binding.WorkType, coalesceKey, ct);
            if (existing is not null) return Handle(existing.Id); // collapse concurrent / duplicate submit — no save
        }

        // Conditional save: persist only when the work-item is new or has changed since the last save.
        // First-submit correctness: the entity must be in the store before its first JobRecord is appended.
        var current = await binding.Load(workId, ct);
        var incomingSnapshot = Snapshot(workItem);
        if (current is null || incomingSnapshot is null || Snapshot(current) != incomingSnapshot)
            await binding.Save(workItem, ct);

        var gateKey = await ResolveGateKey(binding, workItem, ct);
        var rec = JobRecordFactory.Create(binding, policy, workItem, workId, action, now, after, Correlation(), gateKey, carrier);
        await _ledger.Append(rec, ct);

        // Transactional outbox: inside an ambient transaction the work-item Save + the ledger Append enlist (TrackSave)
        // and only become claimable on commit (discarded on rollback). Don't inline-drain or push-notify mid-transaction —
        // the row isn't visible yet; the worker (or a post-commit drain) picks it up.
        if (!EntityContext.InTransaction) _transport.Notify();          // wake the worker now (push-dispatch)
        if (_options.Mode == JobMode.Inline && !EntityContext.InTransaction) await _orchestrator.DrainAsync(ct);
        return Handle(rec.Id);
    }

    public async Task<int> SubmitManyAsync(IEnumerable<object> workItems, string action, TimeSpan? after, CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var correlation = Correlation();
        var carrier = Carrier();   // one ambient snapshot for the whole batch (all items share the submit context)
        var batch = new List<JobRecord>();

        foreach (var workItem in workItems)
        {
            var binding = _registry.Require(workItem.GetType().FullName!);
            var policy = binding.ResolvePolicy(action, _options);
            var workId = binding.GetId(workItem);

            // Coalesce check BEFORE any entity save (ambient folded in — per-tenant dedup).
            var coalesceKey = JobCoalesce.FoldAmbient(binding.CoalesceKey(workItem, action), carrier);
            if (coalesceKey is not null && await _ledger.FindActiveByCoalesceKey(binding.WorkType, coalesceKey, ct) is not null)
                continue;

            // Conditional save: persist only when new or changed.
            var current = await binding.Load(workId, ct);
            var incomingSnapshot = Snapshot(workItem);
            if (current is null || incomingSnapshot is null || Snapshot(current) != incomingSnapshot)
                await binding.Save(workItem, ct);

            var gateKey = await ResolveGateKey(binding, workItem, ct);
            batch.Add(JobRecordFactory.Create(binding, policy, workItem, workId, action, now, after, correlation, gateKey, carrier));
        }

        if (batch.Count > 0) await _ledger.AppendMany(batch, ct);
        if (batch.Count > 0 && !EntityContext.InTransaction) _transport.Notify();
        if (_options.Mode == JobMode.Inline && !EntityContext.InTransaction) await _orchestrator.DrainAsync(ct);
        return batch.Count;
    }

    public async Task<JobHandle> TriggerAsync(string workType, string action, CancellationToken ct)
    {
        var binding = _registry.Require(workType);
        var policy = binding.ResolvePolicy(action, _options);
        var now = _clock.GetUtcNow();
        // The singleton is ephemeral — it must NOT be persisted into the consumer's entity collection. The orchestrator
        // creates a fresh instance via binding.NewSingleton at execution time (no store round-trip on the hot path).
        var workItem = binding.NewSingleton(SingletonWorkId);
        var carrier = Carrier();

        var coalesceKey = JobCoalesce.FoldAmbient(binding.CoalesceKey(workItem, action), carrier);
        if (coalesceKey is not null)
        {
            var existing = await _ledger.FindActiveByCoalesceKey(binding.WorkType, coalesceKey, ct);
            if (existing is not null) return Handle(existing.Id);
        }

        var gateKey = await ResolveGateKey(binding, workItem, ct);
        var rec = JobRecordFactory.Create(binding, policy, workItem, SingletonWorkId, action, now, null, Correlation(), gateKey, carrier);
        await _ledger.Append(rec, ct);

        if (!EntityContext.InTransaction) _transport.Notify();
        if (_options.Mode == JobMode.Inline && !EntityContext.InTransaction) await _orchestrator.DrainAsync(ct);
        return Handle(rec.Id);
    }

    public async Task CancelWorkAsync(string workType, string workId, CancellationToken ct)
    {
        var jobs = await _ledger.Query(new JobQuery(WorkType: workType, WorkId: workId), ct);
        var now = _clock.GetUtcNow();
        foreach (var rec in jobs)
        {
            if (rec.IsTerminal) continue;
            rec.CancelRequestedAt = now;
            if (rec.Status == JobStatus.Running)
            {
                await _ledger.Update(rec, ct);
                _orchestrator.SignalCancel(rec.Id);
            }
            else
            {
                rec.Transitions.Add(new JobTransition { At = now, From = rec.Status, To = JobStatus.Cancelled, Note = "cancelled (pre-run)" });
                rec.Status = JobStatus.Cancelled;
                rec.LastSettledAt = now;
                await _ledger.Update(rec, ct);
            }
        }
    }

    public async Task<JobStatus?> StatusAsync(string workType, string workId, CancellationToken ct)
    {
        var jobs = await _ledger.Query(new JobQuery(WorkType: workType, WorkId: workId), ct);
        if (jobs.Count == 0) return null;
        return jobs.OrderByDescending(j => j.FirstSubmittedAt).First().Status;
    }

    public Task<IReadOnlyList<JobRecord>> WhereAsync(JobQuery query, CancellationToken ct) => _ledger.Query(query, ct);

    public JobHandle Handle(string jobId) => new(jobId, async (timeout, ct) =>
    {
        var deadline = _clock.GetUtcNow() + timeout;
        while (true)
        {
            var r = await _ledger.Get(jobId, ct);
            if (r is null) return new JobOutcome(JobStatus.Dead, "job not found");
            if (r.IsTerminal) return new JobOutcome(r.Status, r.LastError);
            if (_clock.GetUtcNow() >= deadline) return new JobOutcome(r.Status, "timed out waiting for completion");
            await Task.Delay(TimeSpan.FromMilliseconds(50), _clock, ct);
        }
    });

    private static string? Correlation() => Activity.Current?.TraceId.ToString();

    // Snapshot the Koan context axes on the submitting flow (symmetric with Correlation()). Null in the common case
    // (no cross-cutting axis) — zero allocation, absent on the row.
    private IReadOnlyDictionary<string, string>? Carrier() => _contextCarriers.Capture();

    private static string? Snapshot(object workItem)
    {
        try { return JsonSerializer.Serialize(workItem, workItem.GetType()); }
        catch { return null; }
    }
}
