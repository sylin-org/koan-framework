using System.Linq.Expressions;
using Koan.Data.Abstractions;

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
        DemandDestination();
        await using var operationHorizon = await EnterOperationHorizon(ct).ConfigureAwait(false);
        var progress = new TransferProgress();
        if (HasSameContext()) return Complete(TransferKind.Move, 0, 0, 0, progress);
        var read = 0;
        var copied = 0;
        await using var confirmed = new TransferJournal<TKey>();

        await foreach (var batch in ReadBatches(FromContext, ct).ConfigureAwait(false))
        {
            read = checked(read + batch.Count);
            copied = checked(copied + await WriteBatch(
                batch, FromContext, ToContext, TransferKind.Move, progress, ct).ConfigureAwait(false));
            foreach (var entity in batch)
                await confirmed.Append(entity.Id, ct).ConfigureAwait(false);
        }

        var deleted = await DeleteJournal(confirmed, FromContext, ct).ConfigureAwait(false);
        return Complete(TransferKind.Move, read, copied, deleted, progress);
    }
}
