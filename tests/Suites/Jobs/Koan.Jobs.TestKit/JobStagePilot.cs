using Koan.Data.Core.Model;
using Koan.Jobs;

namespace Koan.Jobs.TestKit;

/// <summary>Outcome of a single stage execution via <see cref="JobStagePilot.RunStageAsync{T}"/>.</summary>
public sealed record StageRunResult(
    JobRunResult Run,
    JobRecord Settled,
    JobRecord? Successor);

/// <summary>
/// In-process stage-handler executor for integration tests. Submits a specific action on a job work-item and
/// drives exactly one orchestrator claim/settle cycle through the real production path. Lets a test assert entity
/// mutations, control signals (<see cref="JobSignal"/>), settle semantics, and the appended successor record
/// without running a full <see cref="JobOrchestrator.DrainAsync"/> loop.
/// <para>Obtain via <see cref="JobsHarness.Pilot"/>.</para>
/// </summary>
public sealed class JobStagePilot
{
    private readonly JobsHarness _harness;

    public JobStagePilot(JobsHarness harness)
    {
        _harness = harness ?? throw new ArgumentNullException(nameof(harness));
    }

    /// <summary>
    /// Persist <paramref name="workItem"/> to the store, enqueue it for <paramref name="action"/>, execute exactly
    /// that one stage through the orchestrator's real claim/settle path, and return the full result.
    /// </summary>
    /// <typeparam name="T">The job work-item type.</typeparam>
    /// <param name="workItem">The work-item to operate on. Its current state is saved before enqueue.</param>
    /// <param name="action">The stage action to execute. Pass an empty string for single-action jobs.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="StageRunResult"/> containing the <see cref="JobRunResult"/> (signals, action, job id),
    /// the settled <see cref="JobRecord"/>, and the successor record appended by a chain advance (null if
    /// the chain stopped or the stage was terminal).
    /// </returns>
    public async Task<StageRunResult> RunStageAsync<T>(T workItem, string action = "", CancellationToken ct = default)
        where T : Entity<T>, IKoanJob<T>
    {
        // Submit through the coordinator: saves the work-item, appends the record, no auto-drain (Mode = Normal).
        var result = await _harness.Driver.RunStageAsync(workItem, action, ct);
        return new StageRunResult(result.Run, result.Settled, result.Successor);
    }
}
