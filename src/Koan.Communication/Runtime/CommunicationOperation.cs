using Koan.Core.Json;

namespace Koan.Communication.Runtime;

internal sealed class CommunicationOperation
{
    private readonly CommunicationRouteDecision _route;
    private readonly TaskCompletionSource<CommunicationSettlementCounts> _settlement =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _enumerated;
    private long _accepted;
    private long _rejected;
    private long _expected;
    private long _delivered;
    private long _filtered;
    private long _failed;
    private long _overflowed;
    private long _settled;
    private int _targetGroups;
    private int _targetGroupsInitialized;
    private int _targetGroupsKnown = 1;
    private int _settlementObservable = 1;
    private int _sealed;
    private int _sourceCompleted;
    private int _awaitsCommit;

    public CommunicationOperation(CommunicationRouteDecision route)
    {
        _route = route;
        if (!route.Adapter.Descriptor.IsBuiltIn)
        {
            _targetGroupsKnown = 0;
            _settlementObservable = 0;
        }
        OperationId = Guid.CreateVersion7();
    }

    public Guid OperationId { get; }

    public void MarkEnumerated() => Interlocked.Increment(ref _enumerated);

    /// <summary>Reads the source now, inside its scope, into copies that a later change to the entities does not reach, and ends this operation as one that
    /// awaits its scope's commit. The publication is a second operation then, whose failure the scope logs, since the commit stands.</summary>
    public async Task<IReadOnlyList<string>> AwaitCommit<TEntity>(IAsyncEnumerable<TEntity> source, CancellationToken ct)
    {
        var copies = new List<string>();
        await foreach (var entity in source.WithCancellation(ct).ConfigureAwait(false))
        {
            MarkEnumerated();
            copies.Add(entity!.ToJson());
        }

        Volatile.Write(ref _settlementObservable, 0);
        Volatile.Write(ref _awaitsCommit, 1);
        Seal(true);
        return copies;
    }

    public static async IAsyncEnumerable<TEntity> Copies<TEntity>(IReadOnlyList<string> copies)
    {
        foreach (var copy in copies) yield return copy.FromJson<TEntity>()!;
        await Task.CompletedTask.ConfigureAwait(false);
    }
    public void MarkRejected() => Interlocked.Increment(ref _rejected);

    public void ReserveAcceptance(int? targetGroups, bool settlementObservable)
    {
        if (targetGroups is < 0) throw new ArgumentOutOfRangeException(nameof(targetGroups));
        if (targetGroups.HasValue)
        {
            if (Interlocked.CompareExchange(ref _targetGroupsInitialized, 1, 0) == 0)
            {
                Volatile.Write(ref _targetGroups, targetGroups.Value);
            }
            else if (Volatile.Read(ref _targetGroups) != targetGroups.Value)
            {
                throw new InvalidOperationException("A Communication adapter changed its target-group count during one operation.");
            }
        }
        else
        {
            Volatile.Write(ref _targetGroupsKnown, 0);
        }

        if (settlementObservable && targetGroups.HasValue)
        {
            Interlocked.Add(ref _expected, targetGroups.Value);
        }
        else
        {
            Volatile.Write(ref _settlementObservable, 0);
        }

        Interlocked.Increment(ref _accepted);
    }

    public void RollBackAcceptance(int? targetGroups, bool settlementObservable)
    {
        Interlocked.Decrement(ref _accepted);
        if (settlementObservable && targetGroups.HasValue)
            Interlocked.Add(ref _expected, -targetGroups.Value);
    }

    public void MarkDelivered()
    {
        Interlocked.Increment(ref _delivered);
        MarkSettled();
    }

    public void MarkFiltered()
    {
        Interlocked.Increment(ref _filtered);
        MarkSettled();
    }

    public void MarkFailed()
    {
        Interlocked.Increment(ref _failed);
        MarkSettled();
    }

    /// <summary>A target whose backlog was over its memory budget: its delivery failed without reaching the handler.</summary>
    public void MarkOverflowed()
    {
        Interlocked.Increment(ref _overflowed);
        MarkFailed();
    }

    public void Seal(bool sourceCompleted)
    {
        if (sourceCompleted)
        {
            Volatile.Write(ref _sourceCompleted, 1);
        }

        if (Interlocked.Exchange(ref _sealed, 1) == 0)
        {
            TryCompleteSettlement();
        }
    }

    public CommunicationOperationSnapshot Snapshot()
        => new(
            OperationId,
            Volatile.Read(ref _enumerated),
            Volatile.Read(ref _accepted),
            Volatile.Read(ref _rejected),
            Volatile.Read(ref _sourceCompleted) == 1,
            Volatile.Read(ref _targetGroupsKnown) == 1 ? Volatile.Read(ref _targetGroups) : null,
            Volatile.Read(ref _settlementObservable) == 1,
            Volatile.Read(ref _awaitsCommit) == 1,
            _route.Channel,
            _route.AdapterId,
            _route.Assurance,
            _settlement.Task);

    private void MarkSettled()
    {
        Interlocked.Increment(ref _settled);
        TryCompleteSettlement();
    }

    private void TryCompleteSettlement()
    {
        if (Volatile.Read(ref _settlementObservable) == 0
            || Volatile.Read(ref _sealed) == 0
            || Volatile.Read(ref _settled) != Volatile.Read(ref _expected))
        {
            return;
        }

        _settlement.TrySetResult(new CommunicationSettlementCounts(
            Volatile.Read(ref _expected),
            Volatile.Read(ref _delivered),
            Volatile.Read(ref _filtered),
            Volatile.Read(ref _failed),
            Volatile.Read(ref _overflowed)));
    }
}
