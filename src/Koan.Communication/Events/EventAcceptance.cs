using Koan.Communication.Infrastructure;
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
        Channel = Constants.Events.DefaultChannel;
        Adapter = Constants.Events.InProcessAdapter;
        Assurance = Constants.Events.ProcessMemoryAssurance;
        _settlement = snapshot.Settlement;
    }

    public Guid OperationId { get; }
    public long Enumerated { get; }
    public long Accepted { get; }
    public long Rejected { get; }
    public bool SourceCompleted { get; }
    public int SubscriptionGroups { get; }
    public string Channel { get; }
    public string Adapter { get; }
    public string Assurance { get; }

    /// <summary>Waits only for this operation's accepted local subscription targets to settle.</summary>
    public async Task<EventSettlement> WaitForSettlement(CancellationToken ct = default)
    {
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
