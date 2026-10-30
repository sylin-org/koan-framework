using System.Collections.Concurrent;
using Koan.Data.Abstractions.Filtering;
using Microsoft.Extensions.Logging;

namespace Koan.Jobs;

/// <summary>
/// Writes a handler's changes back to its work item without losing a write another writer made while the job ran
/// (JOBS-0010). The item is compared as Koan Data stores it (<see cref="WorkItemImage"/>): only the members the handler
/// changed are written, onto the row as it is stored at settle, through a conditional replace whose guard is that row
/// as the settle just read it (<see cref="WorkItemGuard"/>). A member both the handler and another writer changed to
/// different values is a conflict: nothing is written and <see cref="WorkItemConflictException"/> is thrown. A guard
/// miss caused by a concurrent write merges again; a removal while the job ran stands.
/// <para>A member the store cannot compare exactly is left out of the guard before anything is written, so one member
/// never costs the guard of the others. Where the store or the entity's scope cannot replace conditionally, no member
/// can be compared, or the stored values do not round-trip through a guard, the merged row is written by an ordinary
/// save. Where the item cannot be serialized as Koan stores it, the handler's copy is saved whole. Each case is logged —
/// once per work type and reason at Information and every time at Debug — with the window it leaves open: a write
/// landing between the settle's read and its write, or, for a copy saved whole, every write made while the job ran.</para>
/// </summary>
internal sealed class WorkItemWriter(ILogger logger)
{
    private readonly ConcurrentDictionary<string, byte> _reported = new(StringComparer.Ordinal);

    public async Task Persist(JobTypeBinding binding, string workId, object workItem, WorkItemImage? loaded, CancellationToken ct)
    {
        var handled = WorkItemImage.Capture(workItem);
        if (loaded is null || handled is null)
        {
            Unguarded(binding, workId, "the work item cannot be serialized as Koan stores it, so the handler's copy is saved whole", Window.WholeRun);
            await binding.Save(workItem, ct);
            return;
        }

        var changed = loaded.ChangedIn(handled);
        if (changed.Count == 0) return;   // the handler left the item as it was loaded: nothing to write (§17.1)

        for (var attempt = 1; attempt <= Infrastructure.Constants.Settlement.MergeAttempts; attempt++)
        {
            var stored = await binding.Load(workId, ct);
            if (stored is null)
            {
                logger.LogDebug(
                    "Koan.Jobs wrote nothing back to {WorkType}/{WorkId}: the work item was removed while the job ran, and the removal stands.",
                    binding.WorkType, workId);
                return;
            }

            var current = WorkItemImage.Capture(stored);
            if (current is null)
            {
                Unguarded(binding, workId, "the stored work item cannot be serialized as Koan stores it, so the handler's copy is saved whole", Window.WholeRun);
                await binding.Save(workItem, ct);
                return;
            }

            var conflicts = changed.Where(member => !current.Same(loaded, member) && !current.Same(handled, member)).ToList();
            if (conflicts.Count > 0) throw new WorkItemConflictException(binding.WorkType, workId, conflicts);
            if (changed.All(member => current.Same(handled, member))) return;   // the store already holds what the handler wrote

            var merged = current.With(handled, changed, binding.ClrType);
            var (guard, unguardedMembers) = WorkItemGuard.For(stored, binding.GuardSupport());
            if (unguardedMembers.Count > 0)
                Unguarded(binding, workId, $"the store cannot compare {string.Join(", ", unguardedMembers)} exactly, so the guard leaves them out", Window.ReadToWrite);
            if (guard is null)
            {
                Unguarded(binding, workId, "the store cannot compare any member of the row, so the merged row is saved without a guard", Window.ReadToWrite);
                await binding.Save(merged, ct);
                return;
            }

            bool replaced;
            try { replaced = await binding.ReplaceIf(merged, guard, ct); }
            catch (Exception refusal) when (refusal is NotSupportedException or InvalidFilterFieldException or Koan.Data.Abstractions.MappingValueException)
            {
                // The members were admitted one by one, so a refusal here is of the replace itself: a deferred transaction,
                // a managed or read scope, or an Entity variant.
                Unguarded(binding, workId, $"the conditional replace was refused ({refusal.Message}), so the merged row is saved without a guard", Window.ReadToWrite);
                await binding.Save(merged, ct);
                return;
            }
            if (replaced) return;

            // A miss means the row changed after the read, or the read values cannot guard the row. Read again to tell.
            var reread = await binding.Load(workId, ct);
            if (reread is null) continue;   // removed in between: the next attempt lets the removal stand
            if (WorkItemImage.Capture(reread) is { } again && again.SameAs(current))
            {
                Unguarded(binding, workId, "the stored values did not satisfy a guard built from them, so the merged row is saved without a guard", Window.ReadToWrite);
                await binding.Save(merged, ct);
                return;
            }
        }

        throw new WorkItemConflictException(binding.WorkType, workId,
            ["the stored row kept changing between the settle's read and its write"]);
    }

    /// <summary>The writes another writer may lose to a settle that cannot check everything.</summary>
    private enum Window
    {
        /// <summary>A write landing between the settle's read of the stored row and its write.</summary>
        ReadToWrite,

        /// <summary>Every write made to the item while the job ran: the handler's copy is saved whole.</summary>
        WholeRun,
    }

    /// <summary>Say that a settle writes without detecting everything a concurrent writer may change, and which writes it
    /// may lose: once per work type and reason at Information, every time at Debug.</summary>
    private void Unguarded(JobTypeBinding binding, string workId, string reason, Window window)
    {
        var exposure = window == Window.WholeRun
            ? "Every write another writer made to the item while the job ran is overwritten."
            : "A write that lands between the settle's read and its write is not detected.";
        if (_reported.TryAdd(binding.WorkType + "\n" + reason, 0))
            logger.LogInformation(
                "Koan.Jobs settles {WorkType} with a limited version check: {Reason}. {Exposure}",
                binding.WorkType, reason, exposure);
        else
            logger.LogDebug("Koan.Jobs settled {WorkType}/{WorkId} with a limited version check: {Reason}. {Exposure}",
                binding.WorkType, workId, reason, exposure);
    }
}
