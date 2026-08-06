using Koan.Data.Core.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Jobs.Testing;

/// <summary>
/// Deterministically drives the production Jobs scheduler and orchestrator from an existing Koan host.
/// The driver owns no host, clock, persistence, or assertion framework.
/// </summary>
public sealed class JobsTestDriver
{
    private readonly JobOrchestrator _orchestrator;
    private readonly JobScheduler _scheduler;
    private readonly IJobCoordinator _coordinator;
    private readonly IJobLedger _ledger;

    private JobsTestDriver(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.GetRequiredService<IOptions<JobsOptions>>().Value;
        if (options.EnableWorker)
        {
            throw new InvalidOperationException(
                "Deterministic Jobs driving requires JobsOptions.EnableWorker=false. " +
                "Disable the background worker in the test host before calling JobsTestDriver.From(...).");
        }

        if (options.Mode != JobMode.Normal)
        {
            throw new InvalidOperationException(
                "Deterministic Jobs driving requires JobsOptions.Mode=JobMode.Normal. " +
                "Inline mode executes during submission and leaves no queued stage for the driver.");
        }

        _orchestrator = services.GetRequiredService<JobOrchestrator>();
        _scheduler = services.GetRequiredService<JobScheduler>();
        _coordinator = services.GetRequiredService<IJobCoordinator>();
        _ledger = services.GetRequiredService<IJobLedger>();
    }

    /// <summary>Create a deterministic driver over an already-started Koan host.</summary>
    public static JobsTestDriver From(IServiceProvider services) => new(services);

    /// <summary>Execute exactly one currently-ready job through the production claim/run/settle path.</summary>
    public Task<JobRunResult?> RunOneAsync(CancellationToken ct = default)
        => _orchestrator.ExecuteNextAsync(ct);

    /// <summary>Execute every currently-ready job, including ready chain successors.</summary>
    public Task DrainAsync(CancellationToken ct = default)
        => _orchestrator.DrainAsync(ct);

    /// <summary>Submit recurring actions whose cadence is due at the host's current time.</summary>
    public Task TriggerDueAsync(CancellationToken ct = default)
        => _scheduler.TriggerDueAsync(ct);

    /// <summary>Submit every action declared with the <c>@boot</c> schedule.</summary>
    public Task SubmitBootAsync(CancellationToken ct = default)
        => _scheduler.SubmitBootActionsAsync(ct);

    /// <summary>Reclaim jobs whose execution lease has elapsed.</summary>
    public Task ReapAsync(CancellationToken ct = default)
        => _scheduler.ReapAsync(ct);

    /// <summary>Apply the configured terminal-record retention policy.</summary>
    public Task<int> ArchiveAsync(CancellationToken ct = default)
        => _orchestrator.ArchiveAsync(ct);

    /// <summary>Flush this host's optional in-memory outcome metrics.</summary>
    public Task FlushMetricsAsync(CancellationToken ct = default)
        => _orchestrator.FlushMetricsAsync(ct);

    /// <summary>
    /// Save and submit one Entity-owned stage, execute exactly one job, and return its settled record
    /// and any chain successor.
    /// </summary>
    public async Task<JobStageRunResult> RunStageAsync<T>(
        T workItem,
        string action = "",
        CancellationToken ct = default)
        where T : Entity<T>, IKoanJob<T>
    {
        ArgumentNullException.ThrowIfNull(workItem);

        await _coordinator.SubmitAsync(workItem, action, null, ct);
        var run = await RunOneAsync(ct)
            ?? throw new InvalidOperationException(
                $"No ready job was available for '{typeof(T).Name}' action '{action}'. " +
                "Verify that the record is claimable and no unrelated job is ahead of it.");

        var settled = await _ledger.Get(run.JobId, ct)
            ?? throw new InvalidOperationException($"Settled job record '{run.JobId}' was not found.");

        JobRecord? successor = null;
        if (settled.Status == JobStatus.Completed)
        {
            var siblings = await _ledger.Query(
                new JobQuery(WorkType: settled.WorkType, WorkId: settled.WorkId), ct);
            successor = siblings
                .Where(record => record.Id != settled.Id)
                .OrderByDescending(record => record.FirstSubmittedAt)
                .FirstOrDefault();
        }

        return new JobStageRunResult(run, settled, successor);
    }
}

/// <summary>The observable result of one deterministic Jobs stage execution.</summary>
public sealed record JobStageRunResult(
    JobRunResult Run,
    JobRecord Settled,
    JobRecord? Successor);
