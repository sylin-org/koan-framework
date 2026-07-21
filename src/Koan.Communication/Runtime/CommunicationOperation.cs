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
    private long _settled;
    private int _targetGroups;
    private int _targetGroupsInitialized;
    private int _targetGroupsKnown = 1;
    private int _settlementObservable = 1;
    private int _sealed;
    private int _sourceCompleted;

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
            Volatile.Read(ref _failed)));
    }
}
