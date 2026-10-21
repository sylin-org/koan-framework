using System.Linq.Expressions;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Transfers;

public sealed class CopyTransferBuilder<TEntity, TKey>
    : EntityTransferBuilderBase<TEntity, TKey, CopyTransferBuilder<TEntity, TKey>>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    internal CopyTransferBuilder(Expression<Func<TEntity, bool>>? predicate)
        : base(predicate)
    {
    }

    public async Task<TransferResult<TKey>> Run(CancellationToken ct = default)
    {
        var progress = new TransferProgress();
        await using var operationHorizon = await Admit(TransferKind.Copy, writesFrom: false, writesTo: true, progress, ct).ConfigureAwait(false);
        using var fidelity = EnterFidelity();
        if (operationHorizon is null) return Complete(TransferKind.Copy, 0, 0, 0, progress);
        var read = 0L;
        var copied = 0L;

        await foreach (var batch in ReadBatches(FromContext, progress, ct).ConfigureAwait(false))
        {
            read = checked(read + batch.Count);
            copied = checked(copied + await WriteBatch(
                batch, TransferDirection.IntoDestination, TransferKind.Copy, progress, ct).ConfigureAwait(false));
        }

        return Complete(TransferKind.Copy, read, copied, 0, progress);
    }
}
