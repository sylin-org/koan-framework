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
        DemandDestination();
        var progress = new TransferProgress();
        if (HasSameContext()) return Complete(TransferKind.Copy, 0, 0, 0, progress);
        var read = 0;
        var copied = 0;

        await foreach (var batch in ReadBatches(FromContext, ct).ConfigureAwait(false))
        {
            read = checked(read + batch.Count);
            copied = checked(copied + await WriteBatch(
                batch, FromContext, ToContext, TransferKind.Copy, progress, ct).ConfigureAwait(false));
        }

        return Complete(TransferKind.Copy, read, copied, 0, progress);
    }
}
