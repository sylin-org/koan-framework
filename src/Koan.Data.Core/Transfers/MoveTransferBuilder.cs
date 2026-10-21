using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Core.Polymorphism;
using Newtonsoft.Json;

namespace Koan.Data.Core.Transfers;

public sealed class MoveTransferBuilder<TEntity, TKey>
    : EntityTransferBuilderBase<TEntity, TKey, MoveTransferBuilder<TEntity, TKey>>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    internal MoveTransferBuilder(Expression<Func<TEntity, bool>>? predicate)
        : base(predicate)
    {
    }

    public async Task<TransferResult<TKey>> Run(CancellationToken ct = default)
    {
        var progress = new TransferProgress();
        await using var operationHorizon = await Admit(TransferKind.Move, progress, ct).ConfigureAwait(false);
        using var fidelity = EnterFidelity();
        var read = 0L;
        var copied = 0L;
        await using var confirmed = new TransferJournal<MovedRow>();

        await foreach (var batch in ReadBatches(FromContext, progress, ct).ConfigureAwait(false))
        {
            read = checked(read + batch.Count);
            // Fingerprint the rows as read, before write preparation stamps the instances for the destination.
            var moved = batch.Select(entity => new MovedRow(entity.Id, Fingerprint(entity))).ToArray();
            copied = checked(copied + await WriteBatch(
                batch, FromContext, ToContext, TransferKind.Move, progress, ct).ConfigureAwait(false));
            foreach (var row in moved)
                await confirmed.Append(row, ct).ConfigureAwait(false);
        }

        var conflicts = new List<TransferConflict<TKey>>();
        var deleted = await DeleteUnchanged(confirmed, conflicts, progress, ct).ConfigureAwait(false);
        return Complete(TransferKind.Move, read, copied, deleted, progress, conflicts);
    }

    /// <summary>
    /// Deletes from the origin only the copied rows that are still there and unchanged since they were read. A row
    /// another writer changed in the meantime stays in the origin and is reported as a conflict, because the
    /// destination holds its older version; a row another writer removed is reported and not counted. A present,
    /// unchanged row that then fails to delete is still an exact-receipt failure. The check and the delete are two
    /// steps, so a write landing between them is not detected.
    /// </summary>
    private async Task<long> DeleteUnchanged(
        TransferJournal<MovedRow> journal,
        List<TransferConflict<TKey>> conflicts,
        TransferProgress progress,
        CancellationToken ct)
    {
        var deleted = 0L;
        var changed = 0L;
        var absent = 0L;
        await foreach (var rows in journal.ReadBatches(BatchSize, ct).ConfigureAwait(false))
        {
            using var scope = FromContext?.Apply();
            var current = await Data<TEntity, TKey>.GetMany(rows.Select(row => row.Id).ToArray(), ct)
                .ConfigureAwait(false);
            var unchanged = new List<TKey>(rows.Count);
            for (var index = 0; index < rows.Count; index++)
            {
                if (current[index] is not { } entity) absent++;
                else if (Fingerprint(entity) == rows[index].Fingerprint) unchanged.Add(rows[index].Id);
                else
                {
                    changed++;
                    if (conflicts.Count < BatchSize)
                        conflicts.Add(new TransferConflict<TKey>(rows[index].Id,
                            "The source row changed after it was copied; it was kept in the source."));
                }
            }

            if (unchanged.Count == 0) continue;
            var affected = await Data<TEntity, TKey>.DeleteMany(unchanged, ct).ConfigureAwait(false);
            if (affected != unchanged.Count)
                throw new BulkMutationReceiptRejectedException(
                    typeof(TEntity).FullName ?? typeof(TEntity).Name,
                    unchanged.Count,
                    affected,
                    DataCommitOutcome.Unknown);
            deleted = checked(deleted + affected);
        }

        if (changed != 0)
            progress.Warn($"{changed} moved row(s) changed in the source after they were copied and were kept there, " +
                       "because the destination holds their older version. Conflicts lists " +
                       (changed > conflicts.Count ? $"the first {conflicts.Count}" : "them") +
                       "; run the Move again to carry the newer versions.");
        if (absent != 0)
            progress.Warn($"{absent} moved row(s) were no longer in the source when the Move removed its copies; " +
                       "another writer removed them, and they are not counted as deleted.");
        return deleted;
    }

    private static string Fingerprint(TEntity entity)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            EntityJsonSerialization.SerializeDocumentToken(entity).ToString(Formatting.None))));

    private sealed record MovedRow(TKey Id, string Fingerprint);
}
