using Koan.Communication.Runtime;

namespace Koan.Communication;

/// <summary>A fixed-size summary of one Entity Transport publication operation.</summary>
public sealed class TransportAcceptance
{
    private readonly Task<CommunicationSettlementCounts> _settlement;

    internal TransportAcceptance(CommunicationOperationSnapshot snapshot)
    {
        OperationId = snapshot.OperationId;
        Enumerated = snapshot.Enumerated;
        Accepted = snapshot.Accepted;
        Rejected = snapshot.Rejected;
        SourceCompleted = snapshot.SourceCompleted;
        ReceiverGroups = snapshot.TargetGroups;
        SettlementObservable = snapshot.SettlementObservable;
        Channel = snapshot.Channel;
        Adapter = snapshot.Adapter;
        Assurance = snapshot.Assurance;
        _settlement = snapshot.Settlement;
    }

    public Guid OperationId { get; }
    public long Enumerated { get; }
    public long Accepted { get; }
    public long Rejected { get; }
    public bool SourceCompleted { get; }
    /// <summary>Known publisher-side receiver groups, or null when the provider cannot observe remote topology.</summary>
    public int? ReceiverGroups { get; }
    public bool SettlementObservable { get; }
    public string Channel { get; }
    public string Adapter { get; }
    public string Assurance { get; }

    /// <summary>Waits only for this operation's accepted local receiver targets to settle.</summary>
    public async Task<TransportSettlement> WaitForSettlement(CancellationToken ct = default)
    {
        if (!SettlementObservable)
        {
            throw new TransportException(
                TransportException.FailureKind.SettlementUnavailable,
                $"Entity Transport operation {OperationId} was accepted by '{Adapter}', but that provider does not " +
                "return remote handler settlement to the publisher. Inspect receiver and provider facts instead.",
                this);
        }

        var counts = ct.CanBeCanceled
            ? await _settlement.WaitAsync(ct).ConfigureAwait(false)
            : await _settlement.ConfigureAwait(false);
        return new TransportSettlement(
            OperationId,
            counts.Expected,
            counts.Delivered,
            counts.Filtered,
            counts.Failed);
    }
}
