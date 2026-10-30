namespace Koan.Jobs;

/// <summary>
/// Writes a handler's changes back to its work item without undoing what another writer did while the job ran
/// (JOBS-0010). The item is compared as Koan Data stores it (<see cref="WorkItemImage"/>): only the members the handler
/// changed are written, onto the row as it is stored at settle, by an ordinary save. A member both the handler and another
/// writer changed to different values is a conflict: nothing is written and <see cref="WorkItemConflictException"/> fails
/// the attempt, so the job runs again on the stored item. A removal while the job ran stands. Where the item cannot be
/// serialized as Koan stores it, the handler's copy is saved whole.
/// </summary>
internal static class WorkItemWriter
{
    public static async Task Persist(JobTypeBinding binding, string workId, object workItem, WorkItemImage? loaded, CancellationToken ct)
    {
        var handled = WorkItemImage.Capture(workItem);
        if (loaded is null || handled is null)
        {
            await binding.Save(workItem, ct);
            return;
        }

        var changed = loaded.ChangedIn(handled);
        if (changed.Count == 0) return;   // the handler left the item as it was loaded: nothing to write (§17.1)

        var stored = await binding.Load(workId, ct);
        if (stored is null) return;       // removed while the job ran: the removal stands

        var current = WorkItemImage.Capture(stored);
        if (current is null)
        {
            await binding.Save(workItem, ct);
            return;
        }

        var conflicts = changed.Where(member => !current.Same(loaded, member) && !current.Same(handled, member)).ToList();
        if (conflicts.Count > 0) throw new WorkItemConflictException(binding.WorkType, workId, conflicts);
        if (changed.All(member => current.Same(handled, member))) return;   // the store already holds what the handler wrote

        await binding.Save(current.With(handled, changed, binding.ClrType), ct);
    }
}
