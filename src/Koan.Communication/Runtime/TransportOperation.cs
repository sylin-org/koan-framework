using Koan.Communication.Infrastructure;

namespace Koan.Communication.Runtime;

internal sealed class TransportOperation
{
    private readonly int _receiverGroups;
    private readonly TaskCompletionSource<TransportSettlement> _settlement =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _enumerated;
    private long _accepted;
    private long _rejected;
    private long _expected;
    private long _delivered;
    private long _filtered;
    private long _failed;
    private long _settled;
    private int _sealed;
    private int _sourceCompleted;

    public TransportOperation(int receiverGroups)
    {
        _receiverGroups = receiverGroups;
        OperationId = Guid.CreateVersion7();
    }

    public Guid OperationId { get; }

    public void MarkEnumerated() => Interlocked.Increment(ref _enumerated);
    public void MarkRejected() => Interlocked.Increment(ref _rejected);

    public void ReserveAcceptance()
    {
        Interlocked.Increment(ref _accepted);
        Interlocked.Add(ref _expected, _receiverGroups);
    }

    public void RollBackAcceptance()
    {
        Interlocked.Decrement(ref _accepted);
        Interlocked.Add(ref _expected, -_receiverGroups);
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

    public TransportAcceptance Snapshot()
        => new(
            OperationId,
            Volatile.Read(ref _enumerated),
            Volatile.Read(ref _accepted),
            Volatile.Read(ref _rejected),
            Volatile.Read(ref _sourceCompleted) == 1,
            _receiverGroups,
            Constants.Transport.DefaultChannel,
            Constants.Transport.InProcessAdapter,
            Constants.Transport.ProcessMemoryAssurance,
            _settlement.Task);

    private void MarkSettled()
    {
        Interlocked.Increment(ref _settled);
        TryCompleteSettlement();
    }

    private void TryCompleteSettlement()
    {
        if (Volatile.Read(ref _sealed) == 0
            || Volatile.Read(ref _settled) != Volatile.Read(ref _expected))
        {
            return;
        }

        _settlement.TrySetResult(new TransportSettlement(
            OperationId,
            Volatile.Read(ref _expected),
            Volatile.Read(ref _delivered),
            Volatile.Read(ref _filtered),
            Volatile.Read(ref _failed)));
    }
}
