using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Koan.Jobs;

/// <inheritdoc/>
public sealed class JobCoordinator : IJobCoordinator
{
    private readonly IJobLedger _ledger;
    private readonly JobTypeRegistry _registry;
    private readonly JobOrchestrator _orchestrator;
    private readonly JobsOptions _options;
    private readonly TimeProvider _clock;

    public JobCoordinator(IJobLedger ledger, JobTypeRegistry registry, JobOrchestrator orchestrator,
        IOptions<JobsOptions> options, TimeProvider clock)
    {
        _ledger = ledger;
        _registry = registry;
        _orchestrator = orchestrator;
        _options = options.Value;
        _clock = clock;
    }

    public async Task<JobHandle> SubmitAsync(object workItem, string action, TimeSpan? after, CancellationToken ct)
    {
        var binding = _registry.Require(workItem.GetType().FullName!);
        var policy = binding.ResolvePolicy(action, _options);
        var workId = binding.GetId(workItem);
        await binding.Save(workItem, ct);

        var now = _clock.GetUtcNow();

        var coalesceKey = binding.CoalesceKey(workItem, action);
        if (coalesceKey is not null)
        {
            var existing = await _ledger.FindActiveByCoalesceKey(binding.WorkType, coalesceKey, ct);
            if (existing is not null) return Handle(existing.Id); // collapse concurrent / duplicate submit
        }

        var rec = JobRecordFactory.Create(binding, policy, workItem, workId, action, now, after, Correlation());
        await _ledger.Append(rec, ct);

        if (_options.Mode == JobMode.Inline) await _orchestrator.DrainAsync(ct);
        return Handle(rec.Id);
    }

    public async Task<int> SubmitManyAsync(IEnumerable<object> workItems, string action, TimeSpan? after, CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var correlation = Correlation();
        var batch = new List<JobRecord>();

        foreach (var workItem in workItems)
        {
            var binding = _registry.Require(workItem.GetType().FullName!);
            var policy = binding.ResolvePolicy(action, _options);
            var workId = binding.GetId(workItem);
            await binding.Save(workItem, ct);

            var coalesceKey = binding.CoalesceKey(workItem, action);
            if (coalesceKey is not null && await _ledger.FindActiveByCoalesceKey(binding.WorkType, coalesceKey, ct) is not null)
                continue;

            batch.Add(JobRecordFactory.Create(binding, policy, workItem, workId, action, now, after, correlation));
        }

        if (batch.Count > 0) await _ledger.AppendMany(batch, ct);
        if (_options.Mode == JobMode.Inline) await _orchestrator.DrainAsync(ct);
        return batch.Count;
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
}
