namespace Koan.Communication;

/// <summary>A fixed-size summary of one Entity Transport publication operation.</summary>
public sealed class TransportAcceptance
{
    private readonly Task<TransportSettlement> _settlement;

    internal TransportAcceptance(
        Guid operationId,
        long enumerated,
        long accepted,
        long rejected,
        bool sourceCompleted,
        int receiverGroups,
        string channel,
        string adapter,
        string assurance,
        Task<TransportSettlement> settlement)
    {
        OperationId = operationId;
        Enumerated = enumerated;
        Accepted = accepted;
        Rejected = rejected;
        SourceCompleted = sourceCompleted;
        ReceiverGroups = receiverGroups;
        Channel = channel;
        Adapter = adapter;
        Assurance = assurance;
        _settlement = settlement;
    }

    public Guid OperationId { get; }
    public long Enumerated { get; }
    public long Accepted { get; }
    public long Rejected { get; }
    public bool SourceCompleted { get; }
    public int ReceiverGroups { get; }
    public string Channel { get; }
    public string Adapter { get; }
    public string Assurance { get; }

    /// <summary>Waits only for this operation's accepted local receiver targets to settle.</summary>
    public Task<TransportSettlement> WaitForSettlement(CancellationToken ct = default)
        => ct.CanBeCanceled ? _settlement.WaitAsync(ct) : _settlement;
}
