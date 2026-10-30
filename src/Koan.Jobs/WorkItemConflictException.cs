namespace Koan.Jobs;

/// <summary>
/// A job's handler changed a member of its work item that another writer changed to a different value while the job
/// ran (JOBS-0010). Nothing is written back; the orchestrator runs the job again on the stored item, bounded by
/// <see cref="JobsOptions.MaxWriteConflicts"/>.
/// </summary>
internal sealed class WorkItemConflictException(string workType, string workId, IReadOnlyList<string> members)
    : InvalidOperationException(
        $"The work item {workType}/{workId} changed while the job ran ({string.Join(", ", members)}), " +
        "so the job's changes were not written.")
{
    public IReadOnlyList<string> Members { get; } = members;
}
