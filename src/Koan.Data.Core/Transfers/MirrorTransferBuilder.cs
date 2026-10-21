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
        await using var operationHorizon = await Admit(TransferKind.Mirror, ct).ConfigureAwait(false);
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

        // Do not mutate a numbered-page source while it is being read. First discover the replica identities the
        // authoritative side no longer selects through provider-bounded pages, then delete the confirmed set from
        // the journal. With a predicate, "selects" means present and still matching it there (NK-019): a row that
        // left the predicate on the authoritative side must leave the replica too.
        await foreach (var batch in ReadBatches(replica, ct).ConfigureAwait(false))
        {
            read = checked(read + batch.Count);
            var ids = batch.Select(entity => entity.Id).ToArray();
            var selected = await StillSelected(ids, authoritative, ct).ConfigureAwait(false);
            foreach (var id in ids)
            {
                if (!selected.Contains(id))
                    await absent.Append(id, ct).ConfigureAwait(false);
            }
        }

        var deleted = await DeleteJournal(absent, replica, ct).ConfigureAwait(false);
        return Complete(TransferKind.Mirror, read, copied, deleted, progress);
    }

    /// <summary>
    /// The identities among <paramref name="ids"/> that the authoritative side still selects. Without a predicate
    /// that is every present row; with one it is the store's own answer to <c>predicate AND Id in ids</c>, so a
    /// row is judged by the same semantics that selected it for the copy pass.
    /// </summary>
    private async Task<HashSet<TKey>> StillSelected(
        IReadOnlyList<TKey> ids,
        TransferContextOptions? authoritative,
        CancellationToken ct)
    {
        if (Predicate is null)
            return (await ReadMany(ids, authoritative, ct).ConfigureAwait(false))
                .OfType<TEntity>().Select(entity => entity.Id).ToHashSet();

        var row = Predicate.Parameters[0];
        var among = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            [typeof(TKey)],
            Expression.Constant(ids.ToArray(), typeof(IEnumerable<TKey>)),
            Expression.Property(row, nameof(IEntity<TKey>.Id)));
        var selection = Expression.Lambda<Func<TEntity, bool>>(Expression.AndAlso(Predicate.Body, among), row);
        using var scope = authoritative?.Apply();
        return (await Data<TEntity, TKey>.Query(selection, ct).ConfigureAwait(false))
            .Select(entity => entity.Id).ToHashSet();
    }

    private async Task<TransferResult<TKey>> RunBidirectional(CancellationToken ct)
    {
        var progress = new TransferProgress();
        var conflicts = new List<TransferConflict<TKey>>();
        var read = 0;
        var copied = 0;
        PropertyInfo? timestamp = null;
        if (_conflict == MirrorConflict.Latest)
        {
            timestamp = ResolveTimestampProperty(out var unresolved);
            if (unresolved is not null) AddWarning(unresolved);
        }

        // Writes aimed at the source are deferred until its provider-bounded stream has completed.
        // This prevents a conflict resolution from changing predicate membership between numbered pages.
        // Only the winning identities are journaled; the rows are re-read from the destination at replay, so the
        // source receives exactly what the destination's adapter materializes and never a journal round trip of
        // the entity (NK-001: a JSON round trip drops defaults, recases dictionary keys, loses non-public setters,
        // appends to seeded collections and forgets the family variant).
        await using var targetWins = new TransferJournal<TKey>();
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
                        await targetWins.Append(target.Id, ct).ConfigureAwait(false);
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
                        else if (comparison < 0) await targetWins.Append(target.Id, ct).ConfigureAwait(false);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(_conflict));
                }
            }

            copied = checked(copied + await WriteBatch(
                targetWrites, FromContext, ToContext, TransferKind.Mirror, progress, ct).ConfigureAwait(false));
        }

        await foreach (var ids in targetWins.ReadBatches(BatchSize, ct).ConfigureAwait(false))
        {
            // A winner removed from the destination since the source pass has nothing left to write.
            var winners = (await ReadMany(ids, ToContext, ct).ConfigureAwait(false)).OfType<TEntity>().ToArray();
            copied = checked(copied + await WriteBatch(
                winners, ToContext, FromContext, TransferKind.Mirror, progress, ct).ConfigureAwait(false));
        }

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

    /// <summary>
    /// The property that says which side changed last: the one <c>[Timestamp(OnSave = true)]</c> property, or the
    /// only <c>[Timestamp]</c> property when there is no OnSave one. A set-once creation stamp is never chosen over an
    /// OnSave stamp, because copies share it and would always compare equal (NK-018). Several candidates of the
    /// deciding kind are ambiguous: overlaps are then reported, not guessed.
    /// </summary>
    private static PropertyInfo? ResolveTimestampProperty(out string? unresolved)
    {
        var stamps = typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (Property: property, Attribute: property.GetCustomAttribute<TimestampAttribute>(true)))
            .Where(candidate => candidate.Attribute is not null)
            .ToArray();
        var onSave = stamps.Where(candidate => candidate.Attribute!.OnSave).Select(candidate => candidate.Property).ToArray();
        var deciding = onSave.Length != 0 ? onSave : stamps.Select(candidate => candidate.Property).ToArray();

        unresolved = deciding.Length switch
        {
            1 => null,
            0 => $"No [Timestamp] property found on {typeof(TEntity).Name}; overlapping identities are reported without automatic resolution.",
            _ => $"The latest-write timestamp on {typeof(TEntity).Name} is ambiguous: " +
                 string.Join(", ", deciding.Select(property => property.Name).Order(StringComparer.Ordinal)) +
                 $" are all {(onSave.Length != 0 ? "[Timestamp(OnSave = true)]" : "[Timestamp]")}. Overlapping identities " +
                 "are reported without automatic resolution; keep one OnSave timestamp or choose Source or Destination."
        };
        return deciding.Length == 1 ? deciding[0] : null;
    }

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
