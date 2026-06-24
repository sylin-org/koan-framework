using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Data.SoftDelete;

/// <summary>
/// The soft-delete entity verbs (ARCH-0101 §6 — modules extend the entity surface via C# 14 extension members):
/// instance <c>.HardDelete()</c> / <c>.Restore()</c> and static <c>T.WithDeleted()</c>. Reference = Intent reaches
/// the call site; no source generator, no <c>partial</c>.
/// </summary>
public static class SoftDeleteExtensions
{
    extension<T>(T model) where T : Entity<T>
    {
        /// <summary>
        /// Physically remove this row (purge), bypassing the soft-delete override (ARCH-0101 §4 escape verb). The
        /// bypass is plane-specific AND target-scoped to this exact entity — the delete is STILL read-scoped (tenant /
        /// moderation IDOR), so you can only hard-delete a row you can see, and a cascade delete of a different entity
        /// is NOT bypassed. Enters <c>WithDeleted</c> too, so an already-soft-deleted row can be purged (the recycle bin).
        /// </summary>
        public async Task<bool> HardDelete(CancellationToken ct = default)
        {
            using (OperationOverrideBypass.Enter(typeof(T), model.Id))
            using (SoftDeleteAmbient.Enter())
                return await model.Remove(ct);
        }

        /// <summary>
        /// Restore a soft-deleted row by re-persisting it: a normal save no longer emits <c>__deleted</c>, so the row
        /// becomes visible again. Load the row inside a <c>using (T.WithDeleted())</c> scope first (it is otherwise hidden).
        /// </summary>
        public Task<T> Restore(CancellationToken ct = default) => model.Save(ct);
    }

    extension<T>(T) where T : Entity<T>
    {
        /// <summary>
        /// A scope where reads of <typeparamref name="T"/> include soft-deleted rows (the recycle bin):
        /// <c>using (T.WithDeleted()) { ... }</c>. Off by default — never globally on.
        /// </summary>
        public static IDisposable WithDeleted() => SoftDeleteAmbient.Enter();
    }
}
