using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;

namespace Koan.Data.Core.Transfers;

public sealed class MirrorTransferBuilder<TEntity, TKey>
    : EntityTransferBuilderBase<TEntity, TKey, MirrorTransferBuilder<TEntity, TKey>>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly MirrorMode _mode;
    private MirrorConflict _conflict = MirrorConflict.Latest;

    internal MirrorTransferBuilder(MirrorMode mode, Expression<Func<TEntity, bool>>? predicate)
        : base(predicate)
        => _mode = mode;

    public MirrorTransferBuilder<TEntity, TKey> Conflict(MirrorConflict policy)
    {
        _conflict = policy;
        return this;
    }

    public async Task<TransferResult<TKey>> Run(CancellationToken ct = default)
    {
        DemandDestination();
        await using var operationHorizon = await EnterOperationHorizon(ct).ConfigureAwait(false);
        if (HasSameContext())
            return Complete(TransferKind.Mirror, 0, 0, 0, new TransferProgress());
        return _mode switch
        {
            MirrorMode.Push => await RunOneWay(FromContext, ToContext, ct).ConfigureAwait(false),
            MirrorMode.Pull => await RunOneWay(ToContext, FromContext, ct).ConfigureAwait(false),
            MirrorMode.Bidirectional => await RunBidirectional(ct).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(_mode))
        };
    }

    private async Task<TransferResult<TKey>> RunOneWay(
        TransferContextOptions? authoritative,
        TransferContextOptions? replica,
        CancellationToken ct)
    {
        var progress = new TransferProgress();
        var read = 0;
        var copied = 0;
        await using var absent = new TransferJournal<TKey>();

        await foreach (var batch in ReadBatches(authoritative, ct).ConfigureAwait(false))
        {
            read = checked(read + batch.Count);
            copied = checked(copied + await WriteBatch(
                batch, authoritative, replica, TransferKind.Mirror, progress, ct).ConfigureAwait(false));
        }

        // Do not mutate a numbered-page source while it is being read. First discover replica-only
        // identities through provider-bounded pages, then delete the confirmed set from the journal.
        await foreach (var batch in ReadBatches(replica, ct).ConfigureAwait(false))
        {
            read = checked(read + batch.Count);
            var existing = await ReadMany(batch.Select(entity => entity.Id).ToArray(), authoritative, ct)
                .ConfigureAwait(false);
            for (var index = 0; index < batch.Count; index++)
            {
                if (existing[index] is null)
                    await absent.Append(batch[index].Id, ct).ConfigureAwait(false);
            }
        }

        var deleted = await DeleteJournal(absent, replica, ct).ConfigureAwait(false);
        return Complete(TransferKind.Mirror, read, copied, deleted, progress);
    }

    private async Task<TransferResult<TKey>> RunBidirectional(CancellationToken ct)
    {
        var progress = new TransferProgress();
        var conflicts = new List<TransferConflict<TKey>>();
        var read = 0;
        var copied = 0;
        var timestamp = _conflict == MirrorConflict.Latest ? ResolveTimestampProperty() : null;
        if (_conflict == MirrorConflict.Latest && timestamp is null)
            AddWarning($"No [Timestamp] property found on {typeof(TEntity).Name}; overlapping identities are reported without automatic resolution.");

        // Writes aimed at the source are deferred until its provider-bounded stream has completed.
        // This prevents a conflict resolution from changing predicate membership between numbered pages.
        await using var sourceWrites = new TransferJournal<TEntity>();
        await foreach (var sourceBatch in ReadBatches(FromContext, ct).ConfigureAwait(false))
        {
            read = checked(read + sourceBatch.Count);
            var targetWrites = new List<TEntity>(sourceBatch.Count);
            var targets = await ReadMany(sourceBatch.Select(entity => entity.Id).ToArray(), ToContext, ct)
                .ConfigureAwait(false);
            for (var index = 0; index < sourceBatch.Count; index++)
            {
                var source = sourceBatch[index];
                var target = targets[index];
                if (target is null)
                {
                    targetWrites.Add(source);
                    continue;
                }

                switch (_conflict)
                {
                    case MirrorConflict.Source:
                        targetWrites.Add(source);
                        break;
                    case MirrorConflict.Destination:
                        await sourceWrites.Append(target, ct).ConfigureAwait(false);
                        break;
                    case MirrorConflict.Report:
                        AddConflict(conflicts, source.Id, "Both mirror sides contain this identity.");
                        break;
                    case MirrorConflict.Latest:
                        if (timestamp is null)
                        {
                            AddConflict(conflicts, source.Id, "No timestamp is available to resolve the overlap.");
                            break;
                        }
                        if (!TryCompareTimestamp(timestamp, source, target, out var comparison, out var reason))
                        {
                            AddConflict(conflicts, source.Id, reason ?? "The timestamps cannot be compared.");
                            break;
                        }
                        if (comparison > 0) targetWrites.Add(source);
                        else if (comparison < 0) await sourceWrites.Append(target, ct).ConfigureAwait(false);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(_conflict));
                }
            }

            copied = checked(copied + await WriteBatch(
                targetWrites, FromContext, ToContext, TransferKind.Mirror, progress, ct).ConfigureAwait(false));
        }

        await foreach (var deferred in sourceWrites.ReadBatches(BatchSize, ct).ConfigureAwait(false))
            copied = checked(copied + await WriteBatch(
                deferred, ToContext, FromContext, TransferKind.Mirror, progress, ct).ConfigureAwait(false));

        // A second bounded pass admits only target-only identities. Overlaps were resolved exactly once
        // during the source pass; source-only rows copied above now have a source counterpart and are skipped.
        await foreach (var targetBatch in ReadBatches(ToContext, ct).ConfigureAwait(false))
        {
            read = checked(read + targetBatch.Count);
            var sourceAdds = new List<TEntity>(targetBatch.Count);
            var sources = await ReadMany(targetBatch.Select(entity => entity.Id).ToArray(), FromContext, ct)
                .ConfigureAwait(false);
            for (var index = 0; index < targetBatch.Count; index++)
                if (sources[index] is null) sourceAdds.Add(targetBatch[index]);

            copied = checked(copied + await WriteBatch(
                sourceAdds, ToContext, FromContext, TransferKind.Mirror, progress, ct).ConfigureAwait(false));
        }

        return Complete(TransferKind.Mirror, read, copied, 0, progress, conflicts);
    }

    private void AddConflict(List<TransferConflict<TKey>> conflicts, TKey id, string reason)
    {
        if (conflicts.Count == BatchSize)
            throw new InvalidOperationException(
                $"The bidirectional mirror exceeded its explicit conflict-result bound of {BatchSize}. " +
                "Choose Source or Destination conflict resolution, provide comparable [Timestamp] values, or narrow the predicate.");
        conflicts.Add(new TransferConflict<TKey>(id, reason));
    }

    private static PropertyInfo? ResolveTimestampProperty()
        => typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(property => property.GetCustomAttributes(typeof(TimestampAttribute), true).Length != 0);

    private static bool TryCompareTimestamp(
        PropertyInfo property,
        TEntity left,
        TEntity right,
        out int comparison,
        out string? failure)
    {
        var leftValue = property.GetValue(left);
        var rightValue = property.GetValue(right);
        if (leftValue is null || rightValue is null)
        {
            comparison = 0;
            failure = "A timestamp value is missing.";
            return false;
        }

        switch (leftValue)
        {
            case DateTime value when rightValue is DateTime other:
                comparison = DateTime.Compare(value, other);
                break;
            case DateTimeOffset value when rightValue is DateTimeOffset other:
                comparison = DateTimeOffset.Compare(value, other);
                break;
            case long value when rightValue is long other:
                comparison = value.CompareTo(other);
                break;
            case int value when rightValue is int other:
                comparison = value.CompareTo(other);
                break;
            case byte[] value when rightValue is byte[] other:
                comparison = StructuralComparisons.StructuralComparer.Compare(value, other);
                break;
            default:
                comparison = 0;
                failure = $"Timestamp type '{property.PropertyType.FullName}' is not supported.";
                return false;
        }

        failure = null;
        return true;
    }
}
