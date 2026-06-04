namespace Koan.Jobs;

/// <summary>
/// The submit/cancel/query entry point the <c>.Job</c>/<c>.Jobs</c> accessor calls (resolved from the ambient host).
/// Hides the ledger + orchestrator behind a small surface; in <see cref="JobMode.Inline"/> it drains synchronously.
/// </summary>
public interface IJobCoordinator
{
    /// <summary>Submit one action on a work-item. Persists the work-item, coalesces duplicates, appends the job.</summary>
    Task<JobHandle> SubmitAsync(object workItem, string action, TimeSpan? after, CancellationToken ct);

    /// <summary>Submit one action across many work-items in a single bulk enqueue.</summary>
    Task<int> SubmitManyAsync(IEnumerable<object> workItems, string action, TimeSpan? after, CancellationToken ct);

    /// <summary>Durably cancel every active job for a work-item (pre-run → terminate; running → cooperative).</summary>
    Task CancelWorkAsync(string workType, string workId, CancellationToken ct);

    /// <summary>Latest job status for a work-item, or null if it has no jobs.</summary>
    Task<JobStatus?> StatusAsync(string workType, string workId, CancellationToken ct);

    /// <summary>Query the ledger (facade / dashboard).</summary>
    Task<IReadOnlyList<JobRecord>> WhereAsync(JobQuery query, CancellationToken ct);

    /// <summary>A handle whose Completion awaits the terminal state of the given job id.</summary>
    JobHandle Handle(string jobId);
}
