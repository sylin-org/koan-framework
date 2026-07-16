using Koan.Communication.Runtime;

namespace Koan.Communication;

/// <summary>A fixed-size summary of one Entity Event publication operation.</summary>
public sealed class EventAcceptance
{
    private readonly Task<CommunicationSettlementCounts> _settlement;

    internal EventAcceptance(CommunicationOperationSnapshot snapshot)
    {
        OperationId = snapshot.OperationId;
        Enumerated = snapshot.Enumerated;
        Accepted = snapshot.Accepted;
        Rejected = snapshot.Rejected;
        SourceCompleted = snapshot.SourceCompleted;
        SubscriptionGroups = snapshot.TargetGroups;
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
    public int? SubscriptionGroups { get; }
    public bool SettlementObservable { get; }
    public string Channel { get; }
    public string Adapter { get; }
    public string Assurance { get; }

    /// <summary>Waits only for this operation's accepted local subscription targets to settle.</summary>
    public async Task<EventSettlement> WaitForSettlement(CancellationToken ct = default)
    {
        if (!SettlementObservable)
        {
            throw new EventException(
                EventException.FailureKind.SettlementUnavailable,
                $"Entity Event operation {OperationId} was accepted by '{Adapter}', but that provider does not " +
                "return remote handler settlement to the publisher. Inspect subscription and provider facts instead.",
                this);
        }

        var counts = ct.CanBeCanceled
            ? await _settlement.WaitAsync(ct).ConfigureAwait(false)
            : await _settlement.ConfigureAwait(false);
        return new EventSettlement(
            OperationId,
            counts.Expected,
            counts.Delivered,
            counts.Filtered,
            counts.Failed);
    }
}
