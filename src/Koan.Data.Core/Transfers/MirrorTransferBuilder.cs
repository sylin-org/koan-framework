using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Transfers;

public sealed class MirrorTransferBuilder<TEntity, TKey> : EntityTransferBuilderBase<TEntity, TKey, MirrorTransferBuilder<TEntity, TKey>>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly MirrorMode _mode;

    internal MirrorTransferBuilder(MirrorMode mode, Expression<Func<TEntity, bool>>? predicate, Func<IQueryable<TEntity>, IQueryable<TEntity>>? queryShaper)
        : base(predicate, queryShaper)
    {
        _mode = mode;
    }

    public async Task<TransferResult<TKey>> Run(CancellationToken cancellationToken = default)
    {
        if (ToContext is null)
            throw new InvalidOperationException("Destination context must be specified via To().");

        var stopwatch = Stopwatch.StartNew();
        var audit = new List<TransferAuditBatch>();
        var batchCounter = 0;
        var totalProcessed = 0;
        var conflicts = new List<TransferConflict<TKey>>();

        int readCount;
        int copied;

        switch (_mode)
        {
            case MirrorMode.Push:
            {
                var items = await FetchEntitiesAsync(FromContext, cancellationToken).ConfigureAwait(false);
                readCount = items.Count;
                var pushProgress = await UpsertBatchesAsync(
                    items,
                    FromContext,
                    ToContext,
                    TransferKind.Mirror,
                    stopwatch,
                    audit,
                    cancellationToken,
                    batchCounter,
                    totalProcessed).ConfigureAwait(false);
                copied = pushProgress.Copied;
                batchCounter = pushProgress.BatchCounter;
                totalProcessed = pushProgress.TotalProcessed;
                break;
            }
            case MirrorMode.Pull:
            {
                var items = await FetchEntitiesAsync(ToContext, cancellationToken).ConfigureAwait(false);
                readCount = items.Count;
                var pullProgress = await UpsertBatchesAsync(
                    items,
                    ToContext,
                    FromContext,
                    TransferKind.Mirror,
                    stopwatch,
                    audit,
                    cancellationToken,
                    batchCounter,
                    totalProcessed).ConfigureAwait(false);
                copied = pullProgress.Copied;
                batchCounter = pullProgress.BatchCounter;
                totalProcessed = pullProgress.TotalProcessed;
                break;
            }
            case MirrorMode.Bidirectional:
            {
                var sourceItems = await FetchEntitiesAsync(FromContext, cancellationToken).ConfigureAwait(false);
                var targetItems = await FetchEntitiesAsync(ToContext, cancellationToken).ConfigureAwait(false);
                readCount = sourceItems.Count + targetItems.Count;

                var timestampProperty = ResolveTimestampProperty();
                if (timestampProperty is null)
                {
                    AddWarning($"No [Timestamp] property found on {typeof(TEntity).Name}; conflicts will be reported without automatic resolution.");
                }

                var copyToDestination = new List<TEntity>();
                var copyToSource = new List<TEntity>();
                var processed = new HashSet<TKey>();
                var targetLookup = targetItems.ToDictionary(e => e.Id);

                foreach (var entity in sourceItems)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var id = entity.Id;
                    if (!targetLookup.TryGetValue(id, out var target))
                    {
                        copyToDestination.Add(entity);
                        continue;
                    }

                    processed.Add(id);

                    if (timestampProperty is null)
                    {
                        conflicts.Add(new TransferConflict<TKey>(id, "No timestamp available to resolve conflict."));
                        continue;
                    }

                    if (!TryCompareTimestamp(timestampProperty, entity, target, out var comparison, out var failureReason))
                    {
                        conflicts.Add(new TransferConflict<TKey>(id, failureReason ?? "Unable to compare timestamp values."));
                        continue;
                    }

                    if (comparison > 0)
                    {
                        copyToDestination.Add(entity);
                    }
                    else if (comparison < 0)
                    {
                        copyToSource.Add(target);
                    }
                }

                foreach (var target in targetItems)
                {
                    if (processed.Contains(target.Id))
                        continue;
                    copyToSource.Add(target);
                }

                copied = 0;
                var destProgress = await UpsertBatchesAsync(
                    copyToDestination,
                    FromContext,
                    ToContext,
                    TransferKind.Mirror,
                    stopwatch,
                    audit,
                    cancellationToken,
                    batchCounter,
                    totalProcessed).ConfigureAwait(false);
                copied += destProgress.Copied;
                batchCounter = destProgress.BatchCounter;
                totalProcessed = destProgress.TotalProcessed;

                var sourceProgress = await UpsertBatchesAsync(
                    copyToSource,
                    ToContext,
                    FromContext,
                    TransferKind.Mirror,
                    stopwatch,
                    audit,
                    cancellationToken,
                    batchCounter,
                    totalProcessed).ConfigureAwait(false);
                copied += sourceProgress.Copied;
                batchCounter = sourceProgress.BatchCounter;
                totalProcessed = sourceProgress.TotalProcessed;
                break;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }

        EmitSummary(TransferKind.Mirror, totalProcessed, stopwatch, audit);
        stopwatch.Stop();

        return new TransferResult<TKey>
        {
            Kind = TransferKind.Mirror,
            ReadCount = readCount,
            CopiedCount = totalProcessed,
            DeletedCount = 0,
            Duration = stopwatch.Elapsed,
            Audit = audit.ToArray(),
            Conflicts = conflicts.ToArray(),
            Warnings = Warnings.ToArray()
        };
    }

    private static PropertyInfo? ResolveTimestampProperty()
        => typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.GetCustomAttributes(typeof(TimestampAttribute), inherit: true).Any());

    private static bool TryCompareTimestamp(PropertyInfo property, TEntity left, TEntity right, out int comparison, out string? failureReason)
    {
        var leftValue = property.GetValue(left);
        var rightValue = property.GetValue(right);

        if (leftValue is null || rightValue is null)
        {
            comparison = 0;
            failureReason = "Timestamp value missing.";
            return false;
        }

        switch (leftValue)
        {
            case DateTime leftDate when rightValue is DateTime rightDate:
                comparison = DateTime.Compare(leftDate, rightDate);
                failureReason = null;
                return true;
            case DateTimeOffset leftOffset when rightValue is DateTimeOffset rightOffset:
                comparison = DateTimeOffset.Compare(leftOffset, rightOffset);
                failureReason = null;
                return true;
            case long leftLong when rightValue is long rightLong:
                comparison = leftLong.CompareTo(rightLong);
                failureReason = null;
                return true;
            case int leftInt when rightValue is int rightInt:
                comparison = leftInt.CompareTo(rightInt);
                failureReason = null;
                return true;
            case byte[] leftBytes when rightValue is byte[] rightBytes:
                comparison = StructuralComparisons.StructuralComparer.Compare(leftBytes, rightBytes);
                failureReason = null;
                return true;
            default:
                comparison = 0;
                failureReason = $"Unsupported timestamp type '{property.PropertyType.FullName}'.";
                return false;
        }
    }
}

