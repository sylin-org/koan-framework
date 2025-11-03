using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Transfers;

public sealed class MoveTransferBuilder<TEntity, TKey> : EntityTransferBuilderBase<TEntity, TKey, MoveTransferBuilder<TEntity, TKey>>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private DeleteStrategy _deleteStrategy = DeleteStrategy.AfterCopy;

    internal MoveTransferBuilder(Expression<Func<TEntity, bool>>? predicate, Func<IQueryable<TEntity>, IQueryable<TEntity>>? queryShaper)
        : base(predicate, queryShaper)
    {
    }

    public MoveTransferBuilder<TEntity, TKey> WithDeleteStrategy(DeleteStrategy strategy)
    {
        _deleteStrategy = strategy;
        return this;
    }

    public async Task<TransferResult<TKey>> Run(CancellationToken cancellationToken = default)
    {
        if (ToContext is null)
            throw new InvalidOperationException("Destination context must be specified via To().");

        var stopwatch = Stopwatch.StartNew();
        var audit = new List<TransferAuditBatch>();
        var batchCounter = 0;
        var totalProcessed = 0;
        var deletedCount = 0;

        var items = await FetchEntitiesAsync(FromContext, cancellationToken);
        var readCount = items.Count;
        var afterCopyIds = _deleteStrategy == DeleteStrategy.AfterCopy ? new List<TKey>() : null;

        foreach (var chunk in items.Chunk(Math.Max(1, BatchSizeValue)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = chunk.ToList();
            using (var destScope = ToContext?.Apply())
            {
                await Data<TEntity, TKey>.UpsertManyAsync(batch, cancellationToken);
            }

            totalProcessed += batch.Count;
            batchCounter++;

            var auditBatch = new TransferAuditBatch(
                TransferKind.Move,
                batchCounter,
                batch.Count,
                totalProcessed,
                SnapshotFor(FromContext),
                SnapshotFor(ToContext),
                stopwatch.Elapsed,
                false);
            audit.Add(auditBatch);
            AuditCallback?.Invoke(auditBatch);

            var ids = batch.Select(e => e.Id).ToList();
            switch (_deleteStrategy)
            {
                case DeleteStrategy.AfterCopy:
                    afterCopyIds!.AddRange(ids);
                    break;
                case DeleteStrategy.Batched:
                    using (var fromScope = FromContext?.Apply())
                    {
                        deletedCount += await Data<TEntity, TKey>.DeleteManyAsync(ids, cancellationToken);
                    }
                    break;
                case DeleteStrategy.Synced:
                    using (var fromScope = FromContext?.Apply())
                    {
                        foreach (var id in ids)
                        {
                            if (await Data<TEntity, TKey>.DeleteAsync(id, cancellationToken))
                            {
                                deletedCount++;
                            }
                        }
                    }
                    break;
            }
        }

        if (_deleteStrategy == DeleteStrategy.AfterCopy && afterCopyIds is { Count: > 0 })
        {
            using var fromScope = FromContext?.Apply();
            deletedCount += await Data<TEntity, TKey>.DeleteManyAsync(afterCopyIds, cancellationToken);
        }

        EmitSummary(TransferKind.Move, totalProcessed, stopwatch, audit);
        stopwatch.Stop();

        return new TransferResult<TKey>
        {
            Kind = TransferKind.Move,
            ReadCount = readCount,
            CopiedCount = totalProcessed,
            DeletedCount = deletedCount,
            Duration = stopwatch.Elapsed,
            Audit = audit.ToArray(),
            Conflicts = Array.Empty<TransferConflict<TKey>>(),
            Warnings = Warnings.ToArray()
        };
    }
}
