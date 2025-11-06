using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Transfers;

public sealed class CopyTransferBuilder<TEntity, TKey> : EntityTransferBuilderBase<TEntity, TKey, CopyTransferBuilder<TEntity, TKey>>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    internal CopyTransferBuilder(Expression<Func<TEntity, bool>>? predicate, Func<IQueryable<TEntity>, IQueryable<TEntity>>? queryShaper)
        : base(predicate, queryShaper)
    {
    }

    public async Task<TransferResult<TKey>> Run(CancellationToken cancellationToken = default)
    {
        if (ToContext is null)
            throw new InvalidOperationException("Destination context must be specified via To().");

        var stopwatch = Stopwatch.StartNew();
        var audit = new List<TransferAuditBatch>();
        var batchCounter = 0;
        var totalProcessed = 0;

        var items = await FetchEntitiesAsync(FromContext, cancellationToken);
        var readCount = items.Count;

        var progress = await UpsertBatchesAsync(
            items,
            FromContext,
            ToContext,
            TransferKind.Copy,
            stopwatch,
            audit,
            cancellationToken,
            batchCounter,
            totalProcessed);

        var copied = progress.Copied;
        batchCounter = progress.BatchCounter;
        totalProcessed = progress.TotalProcessed;

        EmitSummary(TransferKind.Copy, totalProcessed, stopwatch, audit);
        stopwatch.Stop();

        return new TransferResult<TKey>
        {
            Kind = TransferKind.Copy,
            ReadCount = readCount,
            CopiedCount = copied,
            DeletedCount = 0,
            Duration = stopwatch.Elapsed,
            Audit = audit.ToArray(),
            Conflicts = Array.Empty<TransferConflict<TKey>>(),
            Warnings = Warnings.ToArray()
        };
    }
}

